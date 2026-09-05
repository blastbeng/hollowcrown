using Godot;
using Hollowcrown.Networking;
using Hollowcrown.Player;

namespace Hollowcrown.Combat;

/// <summary>
/// Headless combat bot (Vision 7 balance harness): a headless/hidden client
/// body that joins a realm (or spawns offline), walks toward the nearest
/// target and chain-attacks on a timer — no MCP dependency, launch flag
/// --bot. Bots are full ICombatTargets so the server fights them like
/// players; attacks go through the SAME server validation as humans
/// (RequestHitAs -> ValidateAndApply). No model: harness runs are judged
/// from authority logs, not pixels (noted in the commit; playtester arena
/// visuals are unchanged — bots only exist with --bot / HC_BOT).
/// Bot ids: offline bots self-assign from 500 (peer ids are small, world
/// targets start at 1000); a networked bot re-registers under its real ENet
/// peer id at spawn approval, exactly like the local player does.
/// </summary>
public partial class CombatBot : CharacterBody3D, ICombatTarget
{
    private static int _nextBotId = 500;   // offline bot id space

    [Export] public string ClassId { get; set; } = "warden";
    [Export] public string BotName { get; set; } = "Bot";
    [Export] public float AttackInterval { get; set; } = 1.0f;
    [Export] public float Reach { get; set; } = 2.2f;

    public int MaxHp => CombatAuthority.PlayerMaxHp;
    public int Hp { get; private set; }
    public bool IsDead { get; private set; }
    public int CombatId { get; private set; } = -1;
    public Vector3 CombatPosition => GlobalPosition;
    public string DisplayName => BotName;

    private int[] _attackIds = { (int)AttackId.ChainLight, (int)AttackId.ChainMid, (int)AttackId.ChainFinisher };
    private int _attackIndex;
    private float _attackTimer;

    public void AssignCombatId(int id) => CombatId = id;
    public void OnStunned(float seconds) { }
    public void OnStealthed(bool stealthed) { }
    public void OnRooted(float seconds) { }
    public void OnWard(float amount) { }
    public void OnHealed(int hpAfter) => Hp = Mathf.Clamp(hpAfter, 0, MaxHp);

    public void OnHitApplied(int amount, bool heavy, int hpAfter)
    {
        if (amount > 0)
            Hp = hpAfter;
    }

    public void OnKilled()
    {
        IsDead = true;
        Velocity = Vector3.Zero;
        GD.Print($"BOT DOWN ({BotName}) — respawn scheduled server-side");
    }

    public void OnRespawned(int hpAfter, Vector3 spawnPos)
    {
        IsDead = false;
        Hp = hpAfter;
        GlobalPosition = spawnPos;
        Velocity = Vector3.Zero;
        GD.Print($"BOT RESPAWNED ({BotName}) at {spawnPos}");
    }

    public override void _Ready()
    {
        // Class-flavored cadence (BALANCE.md harness section): the bot uses
        // its class's attack ids through the normal server tables.
        switch (ClassId)
        {
            case "nightblade":
                _attackIds = new[] { (int)AttackId.DaggerLight, (int)AttackId.DaggerMid, (int)AttackId.DaggerFinisher };
                AttackInterval = 0.6f;
                Reach = 1.8f;
                break;
            case "revenant":
                _attackIds = new[] { (int)AttackId.BoneSpear };
                AttackInterval = 5.0f;
                Reach = 8.5f;   // spears from the line's edge
                break;
            default:
                _attackIds = new[] { (int)AttackId.ChainLight, (int)AttackId.ChainMid, (int)AttackId.ChainFinisher };
                AttackInterval = 1.0f;
                Reach = 2.2f;
                break;
        }

        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
        });
        FloorSnapLength = 0.4f;
        Hp = MaxHp;
        AddToGroup("combat_targets");
        if (CombatId < 0)
            CombatId = _nextBotId++;           // offline harness id
        CombatAuthority.For(this)?.RegisterBot(this);
        GD.Print($"BOT READY {BotName} ({ClassId}) id={CombatId}");
    }

    public override void _ExitTree()
    {
        if (CombatId > 0)
            CombatAuthority.For(this)?.Unregister(CombatId);
    }

    public override void _PhysicsProcess(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        var authority = CombatAuthority.For(this);
        if (authority is null || !authority.IsAuthorityMode || IsDead)
            return;

        // Target selection (harness determinism): another BOT if one is
        // alive (bot-vs-bot matrix runs), else the nearest player body.
        // Training dummies are never targets — they would pollute the matrix.
        CombatBot? otherBot = null;
        float bestBot = float.MaxValue;
        ICombatTarget? player = null;
        float bestPlayer = float.MaxValue;
        foreach (var node in GetTree().GetNodesInGroup("combat_targets"))
        {
            if (node is not Node3D n || n == this || node is not ICombatTarget t)
                continue;
            if (t.IsDead || t.CombatId <= 0)
                continue;
            var to = n.GlobalPosition - GlobalPosition;
            to.Y = 0f;
            float d = to.Length();
            if (node is CombatBot bot && bot != this)
            {
                if (d < bestBot) { bestBot = d; otherBot = bot; }
            }
            else if (node is not TrainingDummy)
            {
                if (d < bestPlayer) { bestPlayer = d; player = t; }
            }
        }

        ICombatTarget? target = otherBot is not null ? otherBot : player;
        if (target is null)
            return;
        var targetNode = (Node3D)target;
        var toTarget = targetNode.GlobalPosition - GlobalPosition;
        toTarget.Y = 0f;
        float dist = toTarget.Length();
        Vector3 facing = dist > 0.001f ? toTarget.Normalized() : -GlobalBasis.Z;

        // Approach until inside reach, gravity keeps us on the floor.
        Vector3 goal = dist > Reach * 0.8f ? facing * 4.5f : Vector3.Zero;
        Velocity = Velocity.Lerp(goal, 1f - Mathf.Exp(-18f * delta));
        if (!IsOnFloor())
            Velocity += Vector3.Down * 18f * delta;
        RotationDegrees = new Vector3(0f,
            Mathf.RadToDeg(Mathf.Atan2(-facing.X, -facing.Z)), 0f);
        MoveAndSlide();

        _attackTimer -= delta;
        if (dist > Reach || _attackTimer > 0f)
            return;
        _attackTimer = AttackInterval;
        int attackId = _attackIds[_attackIndex];
        _attackIndex = (_attackIndex + 1) % _attackIds.Length;
        // The id is EXPLICIT for offline bots (they are not the local player,
        // peer 1); networked bots ride the normal player path — the socket
        // sender is authoritative there.
        if (CombatId >= 500)
            authority.RequestHitAs(CombatId, target.CombatId, attackId,
                GlobalPosition, facing);
        else
            authority.RequestHit(target.CombatId, attackId,
                GlobalPosition, facing);
        GD.Print($"BOT ATTACK {BotName} -> {target.DisplayName} attack={attackId}");
    }
}
