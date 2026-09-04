using Godot;
using System.Collections.Generic;
using Hollowcrown.World;

namespace Hollowcrown.Player;

/// <summary>
/// Warden class model (Vision 6.8): rigged Quaternius humanoid (CC0,
/// addons/quaternius_ik_rigged — full animation library) replaces the capsule
/// stand-in. Body retinted toward cold-steel plate (Vision 6.11 palette),
/// sword + shield attached to the hand bones for the broad Warden silhouette,
/// and every gameplay state drives a real clip: Idle / Walk / Jog_Fwd
/// locomotion, Sword_Attack chain, Roll dodge, Hit_Chest reaction, Death01.
/// Presentation only — PlayerController owns movement, stamina and combat.
/// </summary>
public partial class WardenModel : Node3D
{
    private const string PackScene = "res://addons/quaternius_ik_rigged/Models_with_rigging/Male_rigged.tscn";
    private const string Lib = "UAL1_Standard/";

    // Runtime-calibrated against the 1.8 m spec (Vision 6.4).
    private const float BodyScale = 1.06f;
    // The glTF model faces +Z; the controller's body -Z faces velocity, so
    // the model wrapper turns around once. Verified against movement.
    private const float ModelYawDegrees = 180f;

    // Palette (Vision 6.11): cold steel armor, bone trim, dark leather straps.
    private static readonly Color ArmorTint = new(0.88f, 0.90f, 0.94f);
    private static readonly Color TrimTint = new(0.74f, 0.62f, 0.45f);

    private const string ClipIdle = Lib + "Idle";
    private const string ClipWalk = Lib + "Walk";
    private const string ClipJog = Lib + "Jog_Fwd";
    private const string ClipRoll = Lib + "Roll";
    private const string ClipAttack = Lib + "Sword_Attack";
    private const string ClipHit = Lib + "Hit_Chest";
    private const string ClipDeath = Lib + "Death01";

    private AnimationPlayer? _anim;
    private readonly List<StandardMaterial3D> _fadeMaterials = new();
    private string? _oneshot;
    private string _locomotionClip = ClipIdle;
    private bool _dead;

    public override void _Ready()
    {
        var packed = GD.Load<PackedScene>(PackScene);
        if (packed is null)
        {
            GD.PushError("WARDEN MODEL: pack scene missing — capsule fallback required");
            return;
        }

        var model = packed.Instantiate();
        // GLTF heroes face +Z; the body -Z faces velocity (FaceMovement) ->
        // turn the model around once inside this wrapper.
        if (model is Node3D root)
            root.RotationDegrees = new Vector3(0f, ModelYawDegrees, 0f);
        AddChild(model);

        // IK helper nodes (influence 0) and their markers are inert in
        // gameplay; free them so nothing samples the rig behind the clips.
        foreach (var node in model.FindChildren("*", "Marker3D", true, false))
            node.QueueFree();
        foreach (var node in model.FindChildren("*", "TwoBoneIK3D", true, false))
            node.QueueFree();

        var skeleton = model.GetNode<Skeleton3D>("Armature/GeneralSkeleton");
        if (skeleton is null)
        {
            GD.PushError("WARDEN MODEL: GeneralSkeleton not found");
            return;
        }

        Retint(skeleton);
        AttachWeapons(skeleton);

        _anim = model.GetNode<AnimationPlayer>("AnimationPlayer");
        if (_anim is null)
            GD.PushError("WARDEN MODEL: AnimationPlayer not found");
        else
        {
            foreach (string clip in _anim.GetAnimationList())
                GD.Print($"WARDEN MODEL clip: {clip}");
            _anim.Play(ClipIdle);
        }

        GD.Print("WARDEN MODEL READY — rigged humanoid (CC0 Quaternius), steel retint, sword+shield sockets");
    }

    /// <summary>Steel-plate retint: overrides keep the source albedo/normal/
    /// roughness textures and tint toward the Vision 6.11 palette. All three
    /// overrides register for the death fade (alpha lerp).</summary>
    private void Retint(Skeleton3D skeleton)
    {
        foreach (var mesh in skeleton.GetChildren())
        {
            if (mesh is not MeshInstance3D mi)
                continue;
            var mat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,   // death fade channel
            };
            switch (mi.Name)
            {
                case "SuperHero_Male":
                    mat.AlbedoTexture = GD.Load<Texture2D>("res://Godot - UE/T_Superhero_Male_Dark.png");
                    mat.NormalEnabled = true;
                    mat.NormalTexture = GD.Load<Texture2D>("res://Godot - UE/T_Superhero_Male_Normal.png");
                    mat.RoughnessTexture = GD.Load<Texture2D>("res://Godot - UE/T_Superhero_Male_Roughness.png");
                    mat.AlbedoColor = new Color(ArmorTint, 1f);
                    mat.Metallic = 0.25f;
                    mat.Roughness = 0.72f;
                    break;
                case "Eyes":
                    mat.AlbedoTexture = GD.Load<Texture2D>("res://Godot - UE/T_Eye_Brown.png");
                    mat.AlbedoColor = new Color(Colors.White, 1f);
                    mat.Roughness = 0.4f;
                    break;
                case "Eyebrows":
                    mat.AlbedoTexture = GD.Load<Texture2D>("res://Godot - UE/T_Hair_1_BaseColor.png");
                    mat.AlbedoColor = new Color(TrimTint, 1f);
                    mat.Roughness = 0.9f;
                    break;
            }
            mi.MaterialOverride = mat;
            _fadeMaterials.Add(mat);
        }
    }

    /// <summary>Weapon sockets on the hand bones (Vision 6.8): greatsword on
    /// the right hand (1.4 m, Vision 6.4), warden shield on the left.</summary>
    private void AttachWeapons(Skeleton3D skeleton)
    {
        var sword = new BoneAttachment3D { Name = "SwordSocket" };
        skeleton.AddChild(sword);
        string? rightHand = FindBone(skeleton, "RightHand");
        if (rightHand is not null)
            sword.BoneName = rightHand;

        var blade = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.07f, 1.25f, 0.015f) },
            Position = new Vector3(0f, 0.62f, 0.02f),
            MaterialOverride = MaterialFactory.WeaponSteel(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        var guard = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.26f, 0.045f, 0.05f) },
            Position = new Vector3(0f, 0.02f, 0.02f),
            MaterialOverride = MaterialFactory.WeaponSteel(),
        };
        var grip = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.018f, Height = 0.22f },
            RotationDegrees = new Vector3(0f, 0f, 90f),
            Position = new Vector3(0f, -0.11f, 0.02f),
            MaterialOverride = MaterialFactory.DarkWood(),
        };
        sword.AddChild(blade);
        sword.AddChild(guard);
        sword.AddChild(grip);

        var shield = new BoneAttachment3D { Name = "ShieldSocket" };
        skeleton.AddChild(shield);
        string? leftHand = FindBone(skeleton, "LeftHand");
        if (leftHand is not null)
            shield.BoneName = leftHand;

        var plate = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.055f },
            RotationDegrees = new Vector3(0f, 0f, 90f),
            Position = new Vector3(0f, -0.06f, 0.10f),
            MaterialOverride = MaterialFactory.ArmorPlate(),
        };
        var boss = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f },
            Position = new Vector3(0f, -0.06f, 0.13f),
            MaterialOverride = MaterialFactory.WeaponSteel(),
        };
        shield.AddChild(plate);
        shield.AddChild(boss);
    }

    private static string? FindBone(Skeleton3D skeleton, string name)
    {
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
            if (skeleton.GetBoneName(i) == name)
                return name;
        return null;
    }

    // ------------------------- animation API ------------------------------

    /// <summary>Continuous locomotion from PlayerController; speed picks the
    /// clip and scales its stride to the real velocity.</summary>
    public void PlayLocomotion(float speed, bool sprinting)
    {
        if (_dead || _anim is null)
            return;

        string next;
        float rate;
        if (speed > 5.2f)  { next = ClipJog;  rate = speed / 6.4f; }
        else if (speed > 0.6f) { next = ClipWalk; rate = speed / 4.3f; }
        else               { next = ClipIdle; rate = 1f; }

        if (_oneshot is not null)
        {
            // One-shot still owns the pose; only refresh the return target.
            _locomotionClip = next;
            return;
        }
        if (_anim!.CurrentAnimation == next && _anim.CurrentAnimation == next)
            _anim.SpeedScale = Mathf.Max(0.6f, rate);
        else
        {
            _locomotionClip = next;
            _anim.Play(next, 0.18f);            // short crossfade
            _anim.SpeedScale = Mathf.Max(0.6f, rate);
        }
    }

    /// <summary>Sword arc swing (WardenChain calls per chain step).</summary>
    public void PlayAttack()
    {
        if (_dead || _anim is null)
            return;
        StartOneShot(ClipAttack);
    }

    /// <summary>Dodge roll clip; i-frames live in the controller.</summary>
    public void PlayRoll(float dodgeDuration)
    {
        if (_dead || _anim is null)
            return;
        StartOneShot(Lib + "Roll");
        var clip = _anim.GetAnimation(Lib + "Roll");
        if (clip is not null && dodgeDuration > 0.05f)
            _anim.SpeedScale = (float)clip.Length / dodgeDuration;
    }

    /// <summary>Hit reaction; skipped while a swing/roll owns the pose.</summary>
    public void PlayHit()
    {
        if (_dead || _anim is null || _oneshot is not null)
            return;
        StartOneShot(ClipHit);
    }

    /// <summary>Death01 one-shot; the pose freezes and the body fades.</summary>
    public void PlayDeath(float fadeAlpha)
    {
        if (_anim is null)
            return;
        if (!_dead)
        {
            _dead = true;
            _oneshot = null;
            _anim.Play(ClipDeath);
            _anim.SpeedScale = 1f;
        }
        float a = Mathf.Clamp(1f - fadeAlpha, 0.15f, 1f);
        foreach (var mat in _fadeMaterials)
            mat.AlbedoColor = new Color(mat.AlbedoColor, a);
    }

    /// <summary>Respawn: reset pose, opacity and state to idle.</summary>
    public void ResetPose()
    {
        _dead = false;
        _oneshot = null;
        _locomotionClip = ClipIdle;
        foreach (var mat in _fadeMaterials)
            mat.AlbedoColor = new Color(mat.AlbedoColor, 1f);
        _anim?.Play(ClipIdle);
    }

    private void StartOneShot(string clip)
    {
        if (_anim is null || !_anim.HasAnimation(clip))
            return;
        _oneshot = clip;
        _anim.Play(clip, 0.08f);
        _anim.SpeedScale = 1f;
    }

    public override void _Process(double delta)
    {
        if (_anim is null || _dead || _oneshot is null)
            return;
        if (!_anim.IsPlaying())
        {
            // One-shot finished: hand the pose back to locomotion.
            _oneshot = null;
            _anim.Play(_locomotionClip, 0.15f);
            _anim.SpeedScale = 1f;
        }
    }
}
