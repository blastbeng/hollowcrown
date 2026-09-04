using Godot;
using System.Collections.Generic;
using Hollowcrown.Combat;
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

    // Runtime-calibrated: head bone at 1.61 m natural. Warden 1.15 -> ~1.85 m
    // (greatsword ~1.44 m); Nightblade 1.12 -> exactly the Vision 6.4 1.8 m
    // spec — the "slim" silhouette comes from the twin 0.35 m daggers, not
    // from shrinking the character below spec.
    private const float BodyScaleWarden = 1.15f;
    private const float BodyScaleNightblade = 1.12f;
    private const float BodyScaleRevenant = 1.15f;
    // The glTF model faces +Z; the controller's body -Z faces velocity, so
    // the model wrapper turns around once. Verified against movement.
    private const float ModelYawDegrees = 180f;

    // Palette (Vision 6.11): cold steel plate via the grayscale-limb shader;
    // warm bone trim, dark leather straps.
    private static readonly Color TrimTint = new(0.58f, 0.50f, 0.36f);

    private const string ClipIdle = Lib + "Idle";
    private const string ClipWalk = Lib + "Walk";
    private const string ClipJog = Lib + "Jog_Fwd";
    private const string ClipRoll = Lib + "Roll";
    private const string ClipHit = Lib + "Hit_Chest";
    private const string ClipDeath = Lib + "Death01";

    private AnimationPlayer? _anim;
    private ShaderMaterial? _bodyMaterial;
    private readonly List<Material> _fadeMaterials = new();
    private string? _oneshot;
    private string _locomotionClip = ClipIdle;
    private bool _dead;

    /// <summary>False (default): the local warden's bone-white plate. True:
    /// colder, darker steel for ENEMY wardens so the silhouettes stay
    /// distinguishable at iso zoom (Vision 6.8).</summary>
    [Export] public bool EnemyTint { get; set; }

    /// <summary>Class variant (Vision 7): same rigged pipeline, different
    /// tint + weapon sockets + attack clips — Warden sword & shield,
    /// Nightblade twin daggers, Revenant staff + hood. The class silhouette
    /// IS the readability (Vision 6.8).</summary>
    [Export] public PlayerClass ClassVariant { get; set; } = PlayerClass.Warden;

    private float _ghostAlpha = 1f;   // stealth (0.35) — folded into death fade

    public override void _Ready()
    {
        var packed = GD.Load<PackedScene>(PackScene);
        if (packed is null)
        {
            GD.PushError("WARDEN MODEL: pack scene missing — capsule fallback required");
            return;
        }

        float bodyScale = ClassVariant switch
        {
            PlayerClass.Nightblade => BodyScaleNightblade,
            PlayerClass.Revenant => BodyScaleRevenant,
            _ => BodyScaleWarden,
        };
        var model = packed.Instantiate();
        // GLTF heroes face +Z; the body -Z faces velocity (FaceMovement) ->
        // turn the model around once inside this wrapper.
        if (model is Node3D root)
        {
            root.RotationDegrees = new Vector3(0f, ModelYawDegrees, 0f);
            root.Scale = new Vector3(bodyScale, bodyScale, bodyScale);
        }
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

        GD.Print($"WARDEN MODEL READY — rigged humanoid (CC0 Quaternius), {PlayerClassInfo.Label(ClassVariant)} variant: retint + {WeaponSummary()} sockets");
    }

    private string WeaponSummary() => ClassVariant switch
    {
        PlayerClass.Nightblade => "twin daggers",
        PlayerClass.Revenant => "staff + hood",
        _ => "sword + shield",
    };

    /// <summary>Class-aware retint: each mesh gets exactly ONE override —
    /// the body the luminance shader, eyes/eyebrows tinted standard mats. All
    /// overrides register for the death fade / stealth ghost (alpha).</summary>
    private void Retint(Skeleton3D skeleton)
    {
        foreach (var mesh in skeleton.GetChildren())
        {
            if (mesh is not MeshInstance3D mi)
                continue;

            Material mat;
            switch (mi.Name)
            {
                case "SuperHero_Male":
                {
                    // Grayscale-luminance shader: the suit texture is warm
                    // tan; only an absolute tint (not a multiply) reads as
                    // the intended material (Vision 6.11).
                    _bodyMaterial = new ShaderMaterial
                    {
                        Shader = GD.Load<Shader>("res://Shaders/steel_limb.gdshader"),
                    };
                    _bodyMaterial.SetShaderParameter("albedo_tex",
                        GD.Load<Texture2D>("res://Godot - UE/T_Superhero_Male_Dark.png"));
                    _bodyMaterial.SetShaderParameter("tint", ClassBodyTint());
                    _bodyMaterial.SetShaderParameter("roughness_v", 0.62f);
                    _bodyMaterial.SetShaderParameter("metallic_v", 0.45f);
                    mat = _bodyMaterial;
                    break;
                }
                case "Eyes":
                    mat = new StandardMaterial3D
                    {
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoTexture = GD.Load<Texture2D>("res://Godot - UE/T_Eye_Brown.png"),
                        AlbedoColor = new Color(Colors.White, 1f),
                        Roughness = 0.4f,
                    };
                    break;
                case "Eyebrows":
                    mat = new StandardMaterial3D
                    {
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoTexture = GD.Load<Texture2D>("res://Godot - UE/T_Hair_1_BaseColor.png"),
                        AlbedoColor = new Color(TrimTint, 1f),
                        Roughness = 0.9f,
                    };
                    break;
                default:
                    mat = new StandardMaterial3D
                    {
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    };
                    break;
            }
            mi.MaterialOverride = mat;
            _fadeMaterials.Add(mat);
        }
    }

    /// <summary>Per-class body tints (Vision 6.11 palette direction): the
    /// Warden reads as plate, the Nightblade as dark charcoal leather, the
    /// Revenant as a near-black robe with an arcane cast. Enemies run the
    /// darker/colder half of each pair.</summary>
    private Color ClassBodyTint() => (ClassVariant, EnemyTint) switch
    {
        (PlayerClass.Warden, false) => new Color(0.62f, 0.66f, 0.74f, 1f),    // bone-white plate
        (PlayerClass.Warden, true) => new Color(0.42f, 0.46f, 0.54f, 1f),     // cold steel #8a919c
        (PlayerClass.Nightblade, false) => new Color(0.32f, 0.30f, 0.28f, 1f),// dark leather
        (PlayerClass.Nightblade, true) => new Color(0.22f, 0.23f, 0.27f, 1f), // darker + cold
        (PlayerClass.Revenant, false) => new Color(0.26f, 0.21f, 0.30f, 1f),  // dark robe, arcane cast
        (PlayerClass.Revenant, true) => new Color(0.18f, 0.14f, 0.23f, 1f),
        _ => new Color(0.62f, 0.66f, 0.74f, 1f),
    };

    /// <summary>Weapon sockets on the hand bones (Vision 6.8), swapped per
    /// class: greatsword + shield / twin 0.35 m daggers / staff + hood.</summary>
    private void AttachWeapons(Skeleton3D skeleton)
    {
        switch (ClassVariant)
        {
            case PlayerClass.Nightblade:
                AttachTwinDaggers(skeleton);
                break;
            case PlayerClass.Revenant:
                AttachStaffAndHood(skeleton);
                break;
            default:
                AttachSwordAndShield(skeleton);
                break;
        }
    }

    /// <summary>Warden: greatsword on the right hand (1.4 m, Vision 6.4),
    /// warden shield on the left.</summary>
    private void AttachSwordAndShield(Skeleton3D skeleton)
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

    /// <summary>Nightblade: a 0.35 m dagger in EACH hand (Vision 6.4) — the
    /// twin-blade silhouette, dark steel so the flash of the swing reads.</summary>
    private void AttachTwinDaggers(Skeleton3D skeleton)
    {
        foreach (var (bone, name) in new[] { ("RightHand", "DaggerSocketR"), ("LeftHand", "DaggerSocketL") })
        {
            var socket = new BoneAttachment3D { Name = name };
            skeleton.AddChild(socket);
            string? found = FindBone(skeleton, bone);
            if (found is not null)
                socket.BoneName = found;

            var blade = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.045f, 0.28f, 0.012f) },
                Position = new Vector3(0f, 0.17f, 0.015f),
                MaterialOverride = MaterialFactory.WeaponSteel(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            var guard = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.10f, 0.02f, 0.035f) },
                Position = new Vector3(0f, 0.025f, 0.015f),
                MaterialOverride = MaterialFactory.DarkStone(),
            };
            var grip = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.11f },
                RotationDegrees = new Vector3(0f, 0f, 90f),
                Position = new Vector3(0f, -0.035f, 0.015f),
                MaterialOverride = MaterialFactory.DarkWood(),
            };
            socket.AddChild(blade);
            socket.AddChild(guard);
            socket.AddChild(grip);
        }
    }

    /// <summary>Revenant: 1.7 m dark staff in the right hand with an arcane
    /// gem, and a pointed cowl over the head — the hooded silhouette.</summary>
    private void AttachStaffAndHood(Skeleton3D skeleton)
    {
        var staffSocket = new BoneAttachment3D { Name = "StaffSocket" };
        skeleton.AddChild(staffSocket);
        string? rightHand = FindBone(skeleton, "RightHand");
        if (rightHand is not null)
            staffSocket.BoneName = rightHand;

        var shaft = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.024f, BottomRadius = 0.03f, Height = 1.7f },
            Position = new Vector3(0f, 0.55f, 0.03f),
            MaterialOverride = MaterialFactory.DarkWood(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        var gem = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f },
            Position = new Vector3(0f, 1.42f, 0.03f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.42f, 0.29f, 0.54f, 1f),   // arcane (Vision 6.11)
                EmissionEnabled = true,
                Emission = new Color(0.42f, 0.29f, 0.54f, 1f),
                EmissionEnergyMultiplier = 1.8f,
                Roughness = 0.3f,
            },
        };
        staffSocket.AddChild(shaft);
        staffSocket.AddChild(gem);

        var hood = new BoneAttachment3D { Name = "HoodSocket" };
        skeleton.AddChild(hood);
        string? head = FindBone(skeleton, "Head");
        if (head is not null)
            hood.BoneName = head;
        var cowl = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.20f, Height = 0.34f },
            Position = new Vector3(0f, 0.06f, -0.02f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.16f, 0.13f, 0.19f, 1f),   // near-black cloth
                Roughness = 0.95f,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        hood.AddChild(cowl);
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

    /// <summary>Class attack clip (WardenChain calls per chain step): the
    /// Warden swings Sword_Attack, the Nightblade stabs with the punch clips
    /// (daggers read as short stabs), the Revenant casts.</summary>
    public void PlayAttack(int chainIndex = 0)
    {
        if (_dead || _anim is null)
            return;
        string clip = ClassVariant switch
        {
            PlayerClass.Nightblade => chainIndex >= 2 ? Lib + "Punch_Cross" : Lib + "Punch_Jab",
            PlayerClass.Revenant => Lib + "Spell_Simple_Shoot",
            _ => Lib + "Sword_Attack",
        };
        StartOneShot(clip);
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

    /// <summary>Fade helper: shader bodies take alpha_v, standard mats take
    /// the AlbedoColor alpha channel (death fade + stealth ghost).</summary>
    private void SetFadeAlpha(float a)
    {
        foreach (var mat in _fadeMaterials)
        {
            if (mat is ShaderMaterial sm)
                sm.SetShaderParameter("alpha_v", a);
            else if (mat is StandardMaterial3D std)
                std.AlbedoColor = new Color(std.AlbedoColor, a);
        }
    }

    /// <summary>Stealth ghost (Vision 7 nightblade): body fades to a dark
    /// shimmer while stealthed; the death fade folds it back in.</summary>
    public void SetGhost(float alpha)
    {
        _ghostAlpha = Mathf.Clamp(alpha, 0.05f, 1f);
        if (!_dead)
            SetFadeAlpha(_ghostAlpha);
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
        float a = Mathf.Clamp(1f - fadeAlpha, 0.15f, 1f) * _ghostAlpha;
        _bodyMaterial?.SetShaderParameter("alpha_v", a);
        SetFadeAlpha(a);
    }

    /// <summary>Respawn: reset pose, opacity and state to idle.</summary>
    public void ResetPose()
    {
        _dead = false;
        _oneshot = null;
        _locomotionClip = ClipIdle;
        _ghostAlpha = 1f;
        _bodyMaterial?.SetShaderParameter("alpha_v", 1f);
        SetFadeAlpha(1f);
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
