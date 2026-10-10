using Lumina.Extensions;

namespace BossMod.Dawntrail.Ultimate.DMU;

sealed class AeroIIIAssault(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.AeroIIIAssault, 15f);

sealed class TheDecisiveBattle(BossModule module) : BossComponent(module)
{
    private Actor? chaosBoss;
    private Actor? exDeathBoss;
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (chaosBoss == null || exDeathBoss == null)
        {
            return;
        }

        var players = Raid.WithoutSlot().SortedByRange(chaosBoss.Position).Take(4).ToList();
        foreach (var player in players)
        {
            Arena.AddLine(chaosBoss.Position, player.Position, Colors.Danger);
        }

        players = [.. Raid.WithoutSlot().SortedByRange(exDeathBoss.Position).Take(4)];
        foreach (var player in players)
        {
            Arena.AddLine(exDeathBoss.Position, player.Position, Colors.Danger);
        }

        foreach (var (_, player) in Raid.WithSlot(true, true, true))
        {
            Arena.Actor(player, player == pc ? Colors.PC : Colors.PlayerGeneric, true);
        }

        var slots = partyConfig.SlotsPerAssignment(Raid);
        if (slots.Length == 0)
        {
            return;
        }

        var assignment = partyConfig[Raid.Members[pcSlot].ContentId];

        if (assignment is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.H2 or
            PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2)
        {
            Arena.ZoneCircleOutline(chaosBoss.Position, 1.25f, Colors.Safe, 2.0f);
        }

        if (assignment is PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.H1 or
            PartyRolesConfig.Assignment.R1 or PartyRolesConfig.Assignment.R2)
        {
            Arena.ZoneCircleOutline(exDeathBoss.Position, 1.25f, Colors.Safe, 2.0f);
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.TheDecisiveBattle)
        {
            chaosBoss = caster;
        }

        if (spell.Action.ID == (uint)AID.TheDecisiveBattle1)
        {
            exDeathBoss = caster;
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is ((uint)AID.TheDecisiveBattle) or ((uint)AID.TheDecisiveBattle1))
        {
            chaosBoss = null;
            exDeathBoss = null;
        }
    }
}

sealed class BowelsOfAgony(BossModule module) : Components.RaidwideCast(module, (uint)AID.BowelsOfAgony);

sealed class Crystals(BossModule module) : BossComponent(module)
{
    public List<(Actor actor, uint colour)> crystals = [];
    public List<(Actor actor, uint colour)> crystalsStored = []; // Used as a reference point for where the crystals were for easier hint logic
    public List<(Actor actor, ActorStatus debuff)> debuffPlayers = [];

    public Element nextElement = Element.None;
    public enum Element { None, Water, Fire, Wind }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.DynamicFluid)
        {
            debuffPlayers.Add((actor, status));
        }

        if (status.ID == (uint)SID.Entropy)
        {
            debuffPlayers.Add((actor, status));
        }

        if (debuffPlayers.Count == 4)
        {
            debuffPlayers.Sort((a, b) => a.debuff.ExpireAt.CompareTo(b.debuff.ExpireAt));
            if (debuffPlayers[0].debuff.ID == (uint)SID.DynamicFluid)
            {
                nextElement = Element.Water;
            }
            else
            {
                nextElement = Element.Fire;
            }
        }
    }

    public override void OnStatusLose(Actor actor, ref ActorStatus status)
    {
        if (status.ID is ((uint)SID.DynamicFluid) or ((uint)SID.Entropy))
        {
            debuffPlayers.RemoveAll(d => d.actor == actor);
        }
    }

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID == (uint)OID.FireP3)
        {
            crystals.Add((actor, Colors.Enemy));
        }

        if (actor.OID == (uint)OID.WaterP3)
        {
            crystals.Add((actor, Color.FromRGBA(0x268BD280).ABGR));
        }

        if (actor.OID == (uint)OID.WindP3)
        {
            crystals.Add((actor, Colors.Safe));
        }

        if (crystals.Count == 3)
        {
            crystalsStored = [.. crystals];
        }
    }

    public override void OnActorDestroyed(Actor actor)
    {
        if (actor.OID == (uint)OID.FireP3)
        {
            crystals.RemoveAll(c => c.actor == actor);
            nextElement = Element.Water;
        }

        if (actor.OID == (uint)OID.WaterP3)
        {
            crystals.RemoveAll(c => c.actor == actor);
            nextElement = Element.Fire;
        }

        if (actor.OID == (uint)OID.WindP3)
        {
            crystals.RemoveAll(c => c.actor == actor);
            nextElement = Element.None;
        }

        if (crystals.Count == 1)
        {
            nextElement = Element.Wind;
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        foreach (var (actor, colour) in crystals)
        {
            Arena.ZoneCircle(actor.Position, 1, colour);
        }
    }
}

sealed class ThunderIII(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ThunderIII, new AOEShapeCircle(15.0f));

// Korean strategy positions. WDir.Rotate uses positive angles counterclockwise.
static class KoreanP3Positions
{
    public static WPos Crystal(WPos center, WDir wind, WDir water, PartyRolesConfig.Assignment role)
    {
        wind = wind.Normalized();
        if (role is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2)
            return center + wind * 4f;
        if (role is PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2)
            return center + wind.Rotate(20f.Degrees()) * 20f;
        var towardsWater = wind.OrthoL().Dot(water) > 0 ? 1f : -1f;
        return center + wind.Rotate((towardsWater * (role == PartyRolesConfig.Assignment.R1 ? 110f : 70f)).Degrees()) * 20f;
    }

    public static WPos Slap(WPos center, WDir facing, bool leftHand, Role role)
    {
        var north = -facing;
        var degrees = leftHand ? role switch { Role.Tank => 45f, Role.Healer => 90f, _ => 135f } : 90f;
        return center + north.Rotate((leftHand ? degrees : -degrees).Degrees()) * 8f;
    }

    public static WPos Blizzard(WPos center, WDir facing, PartyRolesConfig.Assignment role)
    {
        var support = role is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2;
        var left = role is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.R1;
        // Facing Kefka: supports behind him, DPS in front; screen-left is his right.
        return center + ((support ? -facing : facing) + (left ? facing.OrthoR() : facing.OrthoL())).Normalized() * 12f;
    }
}

sealed class WaterCrystal(BossModule module) : Components.GenericBaitProximity(module)
{
    private readonly Crystals? crystals = module.FindComponent<Crystals>();
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Tsunami)
        {
            if (crystals == null)
            {
                return;
            }

            NumCasts++;
        }
    }

    public override void Update()
    {
        CurrentBaits.Clear();

        if (crystals == null || crystals.crystals.Count == 0)
        {
            return;
        }

        if (crystals.nextElement != Crystals.Element.Water)
        {
            return;
        }

        var waterCrystal = crystals.crystals.FirstOrNull(c => c.actor.OID == (uint)OID.WaterP3);
        if (waterCrystal == null)
        {
            return;
        }

        var players = Raid.WithoutSlot().SortedByRange(waterCrystal.Value.actor.Position).ToList();
        for (var i = 0; i < 2; ++i)
        {
            CurrentBaits.Add(new(players[i], new AOEShapeCircle(5.0f)));
        }

        var debuffPlayers = crystals.debuffPlayers.Where(d => d.debuff.ID == (uint)SID.DynamicFluid).Take(2).ToList();
        if (debuffPlayers.Count < 2)
        {
            return;
        }

        for (var i = 0; i < 2; ++i)
        {
            CurrentBaits.Add(new(debuffPlayers[i].actor, new AOEShapeDonut(4.0f, 10.0f)));
        }
    }

    public override void AddGlobalHints(Actor actor, GlobalHints hints)
    {
        hints.Add($"Element: {crystals?.nextElement}");
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (pcSlot < 0 || pcSlot >= 8 || partyConfig.SlotsPerAssignment(Raid).Length == 0 || crystals == null || crystals.crystalsStored.Count != 3)
            return;
        var role = partyConfig[Raid.Members[pcSlot].ContentId];
        var wind = crystals.crystalsStored.First(c => c.actor.OID == (uint)OID.WindP3).actor.Position - Arena.Center;
        var water = crystals.crystalsStored.First(c => c.actor.OID == (uint)OID.WaterP3).actor.Position - Arena.Center;
        Arena.ZoneCircleOutline(KoreanP3Positions.Crystal(Arena.Center, wind, water, role), 1f, Colors.Safe, 2f);
    }
}

sealed class FireCrystal(BossModule module) : Components.GenericBaitProximity(module)
{
    private readonly Crystals? crystals = module.FindComponent<Crystals>();
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Inferno)
        {
            if (crystals == null)
            {
                return;
            }

            NumCasts++;
        }
    }

    public override void Update()
    {
        CurrentBaits.Clear();

        if (crystals == null || crystals.crystals.Count == 0)
        {
            return;
        }

        if (crystals.nextElement != Crystals.Element.Fire)
        {
            return;
        }

        var fireCrystal = crystals.crystals.FirstOrNull(c => c.actor.OID == (uint)OID.FireP3);
        if (fireCrystal == null)
        {
            return;
        }

        var players = Raid.WithoutSlot().SortedByRange(fireCrystal.Value.actor.Position).ToList();
        for (var i = 0; i < 2; ++i)
        {
            CurrentBaits.Add(new(players[i], new AOEShapeDonut(4.0f, 10.0f)));
        }

        var debuffPlayers = crystals.debuffPlayers.Where(d => d.debuff.ID == (uint)SID.Entropy).Take(2).ToList();
        if (debuffPlayers.Count < 2)
        {
            return;
        }

        for (var i = 0; i < 2; ++i)
        {
            CurrentBaits.Add(new(debuffPlayers[i].actor, new AOEShapeCircle(5.0f)));
        }
    }

    public override void AddGlobalHints(Actor actor, GlobalHints hints)
    {
        hints.Add($"Element: {crystals?.nextElement}");
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (pcSlot < 0 || pcSlot >= 8 || partyConfig.SlotsPerAssignment(Raid).Length == 0 || crystals == null || crystals.crystalsStored.Count != 3)
            return;
        var role = partyConfig[Raid.Members[pcSlot].ContentId];
        var wind = crystals.crystalsStored.First(c => c.actor.OID == (uint)OID.WindP3).actor.Position - Arena.Center;
        var water = crystals.crystalsStored.First(c => c.actor.OID == (uint)OID.WaterP3).actor.Position - Arena.Center;
        Arena.ZoneCircleOutline(KoreanP3Positions.Crystal(Arena.Center, wind, water, role), 1f, Colors.Safe, 2f);
    }
}

sealed class LongitudinalLatitudinalImplosion(BossModule module) : Components.GenericAOEs(module, (uint)AID.Shockwave)
{
    private readonly List<AOEInstance> aoes = [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.LongitudinalImplosion)
        {
            aoes.Add(new(new AOEShapeCone(40, 45.Degrees()), spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell)));
            aoes.Add(new(new AOEShapeCone(40, 45.Degrees()), spell.LocXZ, spell.Rotation + 180.Degrees(), Module.CastFinishAt(spell)));
            aoes.Add(new(new AOEShapeCone(40, 45.Degrees()), spell.LocXZ, spell.Rotation + 90.Degrees(), Module.CastFinishAt(spell)));
            aoes.Add(new(new AOEShapeCone(40, 45.Degrees()), spell.LocXZ, spell.Rotation - 90.Degrees(), Module.CastFinishAt(spell)));
        }

        if (spell.Action.ID == (uint)AID.LatitudinalImplosion)
        {
            aoes.Add(new(new AOEShapeCone(40, 45.Degrees()), spell.LocXZ, spell.Rotation + 90.Degrees(), Module.CastFinishAt(spell)));
            aoes.Add(new(new AOEShapeCone(40, 45.Degrees()), spell.LocXZ, spell.Rotation - 90.Degrees(), Module.CastFinishAt(spell)));
            aoes.Add(new(new AOEShapeCone(40, 45.Degrees()), spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell)));
            aoes.Add(new(new AOEShapeCone(40, 45.Degrees()), spell.LocXZ, spell.Rotation + 180.Degrees(), Module.CastFinishAt(spell)));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Shockwave)
        {
            ++NumCasts;
            if (aoes.Count > 0)
            {
                aoes.RemoveAt(0);
            }
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var nextAOEs = aoes.Take(2).ToList();
        return CollectionsMarshal.AsSpan(nextAOEs);
    }
}

sealed class ThunderIIITB(BossModule module) : Components.BaitAwayCast(module, (uint)AID.ThunderIIITBCast, new AOEShapeCircle(5.0f), true)
{
    private Actor? boss = null;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.ThunderIIITBCast)
        {
            boss = caster;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ThunderIIITB)
        {
            NumCasts++;
            if (NumCasts == 2)
            {
                boss = null;
            }
        }
    }

    public override void Update()
    {
        CurrentBaits.Clear();

        if (boss == null)
        {
            return;
        }

        var player = Raid.WithoutSlot().SortedByRange(boss.Position).FirstOrDefault();
        if (player == null)
        {
            return;
        }

        CurrentBaits.Add(new(boss, player, new AOEShapeCircle(5.0f)));
    }
}

sealed class UmbraSmash(BossModule module) : Components.GenericBaitProximity(module)
{
    private readonly Crystals? crystals = module.FindComponent<Crystals>();
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();
    private bool castStarted = false;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.UmbraSmash)
        {
            castStarted = true;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.UmbraSmash)
        {
            ++NumCasts;
            CurrentBaits.Clear();
        }
    }

    public override void Update()
    {
        if (crystals == null || crystals.crystals.Count != 1)
        {
            return;
        }

        if (castStarted)
        {
            return;
        }

        CurrentBaits.Clear();

        var chaosBoss = WorldState.Actors.FirstOrDefault(a => a.OID == (uint)OID.Chaos);
        if (chaosBoss == null)
        {
            return;
        }

        var player = Raid.WithoutSlot().SortedByRange(chaosBoss.Position).LastOrDefault();
        if (player == null)
        {
            return;
        }

        CurrentBaits.Add(new(player, new AOEShapeCircle(15.0f)));
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (crystals == null || crystals.crystals.Count != 1)
        {
            return;
        }

        if (castStarted)
        {
            return;
        }

        var slots = partyConfig.SlotsPerAssignment(Raid);
        if (slots.Length == 0)
        {
            return;
        }
        var assignment = partyConfig[Raid.Members[pcSlot].ContentId];

        var windCrystal = crystals.crystalsStored.First(c => c.actor.OID == (uint)OID.WindP3);

        if (assignment == PartyRolesConfig.Assignment.R1)
        {
            Arena.ZoneCircleOutline((Arena.Center - (windCrystal.actor.Position - Arena.Center).Normalized() * 20f) + new WDir(0, 1.0f), 1.0f, Colors.Safe, 2.0f);
        }
    }
}

sealed class UltimaBlaster(BossModule module) : Components.RaidwideInstant(module, (uint)AID.UltimaBlaster);

sealed class UltimaBlasterLimitCut(BossModule module) : Components.GenericBaitAway(module)
{
    private WPos startPosition;
    private Angle angleRotate;
    private readonly int[] orbNumbers = Utils.MakeArray(8, -1);

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.UltimaBlaster)
        {
            if (startPosition == default)
            {
                startPosition = caster.Position;
            }
            else if (angleRotate == default)
            {
                angleRotate = (startPosition - Arena.Center).OrthoL().Dot(caster.Position - Arena.Center) > 0 ? -45.Degrees() : 45.Degrees();
            }
        }

        if (spell.Action.ID == (uint)AID.UltimaBlasterBait)
        {
            NumCasts++;
            if (CurrentBaits.Count > 0)
            {
                CurrentBaits.RemoveAt(0);
            }
        }
    }

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        var orbNumber = iconID switch
        {
            (uint)IconID.OrbNumber1 => 0,
            (uint)IconID.OrbNumber2 => 1,
            (uint)IconID.OrbNumber3 => 2,
            (uint)IconID.OrbNumber4 => 3,
            (uint)IconID.OrbNumber5 => 4,
            (uint)IconID.OrbNumber6 => 5,
            (uint)IconID.OrbNumber7 => 6,
            (uint)IconID.OrbNumber8 => 7,
            _ => -1
        };

        if (orbNumber >= 0)
        {
            var slot = Raid.FindSlot(actor.InstanceID);
            if (slot >= 0)
            {
                orbNumbers[slot] = orbNumber;

                if (startPosition == default || angleRotate == default)
                {
                    return;
                }

                var player = WorldState.Actors.Find(targetID);
                if (player == null)
                {
                    return;
                }

                CurrentBaits.Add(new(Arena.Center + (startPosition - Arena.Center).Rotate(angleRotate * orbNumber), player, new AOEShapeRect(100.0f, 3.0f), WorldState.FutureTime(12.1f + 0.2f * orbNumber)));
                CurrentBaits.Sort((a, b) => a.Activation.CompareTo(b.Activation));
            }
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);

        foreach (var bait in ActiveBaitsOn(pc))
        {
            Arena.ZoneCircleOutline(Arena.Center + (Arena.Center - bait.Source.Position).Normalized().Rotate(angleRotate * 0.5f) * 19.0f, 0.75f, Colors.Safe);
        }
    }
}

sealed class HeadTailWind(BossModule module) : Components.GenericKnockback(module)
{
    public uint[] Direction = new uint[8];
    private (WPos Origin, DateTime Activation, bool EventHappened) wave;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.VacuumWave)
        {
            wave = new(caster.Position, DateTime.Now, false);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.VacuumWave)
        {
            NumCasts++;
            wave.EventHappened = true;
        }
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        var direction = status.ID switch
        {
            (uint)SID.Headwind => (uint)SID.Headwind,
            (uint)SID.Tailwind => (uint)SID.Tailwind,
            _ => default
        };

        if (direction != default)
        {
            var slot = Raid.FindSlot(actor.InstanceID);
            if (slot >= 0)
            {
                Direction[slot] = direction;
            }
        }
    }

    public override void OnStatusLose(Actor actor, ref ActorStatus status)
    {
        if (status.ID is (uint)SID.Headwind or (uint)SID.Tailwind)
        {
            var slot = Raid.FindSlot(actor.InstanceID);
            if (slot >= 0)
            {
                Direction[slot] = 0;
            }
        }
    }

    public override ReadOnlySpan<Knockback> ActiveKnockbacks(int slot, Actor actor)
    {
        if (wave.Origin != default)
        {
            return new[] { new Knockback(wave.Origin, KnockDistance(slot, actor, wave.Origin), wave.Activation) };
        }

        return [];
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        var direction = Direction[pcSlot];
        if (direction is not (uint)SID.Headwind and not (uint)SID.Tailwind)
        {
            return;
        }

        foreach (var knockback in ActiveKnockbacks(pcSlot, pc))
        {
            var toSource = (knockback.Origin - pc.Position).Normalized();
            var safeFacing = (Direction[pcSlot] == (uint)SID.Headwind ? -toSource : toSource).ToAngle();
            Arena.PathArcTo(pc.Position, 1, (safeFacing + 45f.Degrees()).Rad, (safeFacing - 45f.Degrees()).Rad);
            Arena.PathStroke(false, Colors.Safe);
            Arena.PathArcTo(pc.Position, 1, (safeFacing + 225f.Degrees()).Rad, (safeFacing + 135f.Degrees()).Rad);
            Arena.PathStroke(false, Colors.Danger);
        }
    }

    public override void Update()
    {
        if (wave.EventHappened)
        {
            foreach (var player in Raid.WithoutSlot())
            {
                if (player.LastFrameMovement.Length() / WorldState.Frame.Duration > 15)
                {
                    wave = default;
                    break;
                }
            }
        }
    }

    float KnockDistance(int pcSlot, Actor pc, WPos source)
    {
        var direction = Direction[pcSlot];
        if (direction is not (uint)SID.Headwind and not (uint)SID.Tailwind)
        {
            return 20f;
        }

        var toSource = (source - pc.Position).Normalized();
        var safeFacing = direction == (uint)SID.Headwind ? -toSource : toSource;
        var rel = safeFacing.Normalized().Dot(pc.Rotation.ToDirection());

        if (rel > 0.7071067f)
        {
            return 10f;
        }

        if (rel < -0.7071068f)
        {
            return 40f;
        }

        return 20f;
    }
}

sealed class Cyclone(BossModule module) : Components.GenericStackSpread(module)
{
    private readonly HeadTailWind? windDebuffs = module.FindComponent<HeadTailWind>();
    public int NumCasts = 0;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Cyclone)
        {
            NumCasts++;
        }
    }

    public override void Update()
    {
        Stacks.Clear();

        if (windDebuffs == null)
        {
            return;
        }

        foreach (var (slot, player) in Raid.WithSlot(true, true, true).WhereSlot(p => windDebuffs.Direction[p] is (uint)SID.Headwind or (uint)SID.Tailwind))
        {
            Stacks.Add(new(player, 6.0f));
        }
    }
}

sealed class KefkaMax(BossModule module) : BossComponent(module)
{
    public Actor? boss = null;

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.KefkaMax)
        {
            boss = actor;
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (boss == null)
        {
            return;
        }

        // Taken from actor source file, but with the option of adding a scale
        var actorPos = boss.Position - boss.Rotation.ToDirection() * 20.0f;
        var scale = 2.5f;
        var dir = boss.Rotation.ToDirection();
        var scale07 = scale * 0.7f * dir;
        var scale035 = scale * 0.35f * dir;
        var scale0433 = scale * 0.433f * dir.OrthoR();
        var positionscale035 = actorPos - scale035;
        Arena.AddTriangleFilled(actorPos + scale07, positionscale035 + scale0433, positionscale035 - scale0433, Colors.Object);
    }
}

sealed class SlapHappy(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> aoes = [];
    private WDir? facing;
    private bool leftHand;

    // Big Hands AOEs are 10y apart
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.SlapHappyRightHand or (uint)AID.SlapHappyLeftHand)
        {
            facing = spell.Rotation.ToDirection();
            leftHand = spell.Action.ID == (uint)AID.SlapHappyLeftHand;
        }
        if (spell.Action.ID == (uint)AID.SlapHappyRightHand)
        {
            aoes.Add(new(new AOEShapeCircle(13.0f), Arena.Center + spell.Rotation.ToDirection().OrthoR() * 10.0f + (spell.Rotation.ToDirection().OrthoR() * 10.0f).OrthoR()));
            aoes.Add(new(new AOEShapeCircle(13.0f), Arena.Center + spell.Rotation.ToDirection().OrthoR() * 10.0f));
            aoes.Add(new(new AOEShapeCircle(13.0f), Arena.Center + spell.Rotation.ToDirection().OrthoR() * 10.0f + (spell.Rotation.ToDirection().OrthoR() * 10.0f).OrthoL()));
            aoes.Add(new(new AOEShapeCircle(6.0f), Arena.Center));
        }

        if (spell.Action.ID == (uint)AID.SlapHappyLeftHand)
        {
            aoes.Add(new(new AOEShapeCircle(13.0f), Arena.Center + spell.Rotation.ToDirection().OrthoL() * 10.0f + (spell.Rotation.ToDirection().OrthoL() * 10.0f).OrthoL()));
            aoes.Add(new(new AOEShapeCircle(13.0f), Arena.Center + spell.Rotation.ToDirection().OrthoL() * 10.0f));
            aoes.Add(new(new AOEShapeCircle(13.0f), Arena.Center + spell.Rotation.ToDirection().OrthoL() * 10.0f + (spell.Rotation.ToDirection().OrthoL() * 10.0f).OrthoR()));
            aoes.Add(new(new AOEShapeCircle(6.0f), Arena.Center));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.SlapHappyBigAOE) or ((uint)AID.SlapHappySmallAOE))
        {
            NumCasts++;
            if (aoes.Count > 0)
            {
                aoes.RemoveAt(0);
            }
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        return CollectionsMarshal.AsSpan(aoes);
    }
    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (NumCasts < 4 && facing is { } direction)
            Arena.ZoneCircleOutline(KoreanP3Positions.Slap(Arena.Center, direction, leftHand, pc.Role), 1f, Colors.Safe, 2f);
    }
}

sealed class SlapHappyBaits(BossModule module) : Components.GenericBaitStack(module)
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.SlapHappyLeftHand)
        {
            var party = Raid.WithSlot(true, true, true);
            BitMask allowedTanks = default;
            BitMask allowedHealers = default;
            BitMask allowedDDs = default;

            for (var i = 0; i < party.Length; ++i)
            {
                ref var p = ref party[i];

                if (p.Item2.Role == Role.Tank)
                {
                    allowedTanks.Set(p.Item1);
                }

                if (p.Item2.Role == Role.Healer)
                {
                    allowedHealers.Set(p.Item1);
                }

                if (p.Item2.Role is Role.Melee or Role.Ranged)
                {
                    allowedDDs.Set(p.Item1);
                }
            }

            var addedTank = false;
            var addedHealer = false;
            var addedDD = false;

            for (var i = 0; i < party.Length; ++i)
            {
                ref var player = ref party[i];
                var p = player.Item2;

                if (p.IsDead)
                {
                    continue;
                }

                if (p.Role == Role.Tank)
                {
                    if (!addedTank)
                    {
                        CurrentBaits.Add(new(caster, p, new AOEShapeCone(100, 22.5f.Degrees()), forbidden: ~allowedTanks));
                        addedTank = true;
                    }
                }

                if (p.Role == Role.Healer)
                {
                    if (!addedHealer)
                    {
                        CurrentBaits.Add(new(caster, p, new AOEShapeCone(100, 22.5f.Degrees()), forbidden: ~allowedHealers));
                        addedHealer = true;
                    }
                }

                if (p.Role is Role.Melee or Role.Ranged)
                {
                    if (!addedDD)
                    {
                        CurrentBaits.Add(new(caster, p, new AOEShapeCone(100, 22.5f.Degrees()), forbidden: ~allowedDDs));
                        addedDD = true;
                    }
                }
            }
        }

        if (spell.Action.ID == (uint)AID.SlapHappyRightHand)
        {
            var target = WorldState.Actors.Find(caster.TargetID);
            if (target == null)
            {
                return;
            }

            CurrentBaits.Add(new(caster, target, new AOEShapeCone(100, 30.0f.Degrees())));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.SlapHappyShockingImpactStack) or ((uint)AID.SlapHappyShockwaveRole))
        {
            if (CurrentBaits.Count > 0)
            {
                NumCasts++;
                CurrentBaits.RemoveAt(0);
            }
        }
    }
}

sealed class DamningEdict(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DamningEdict, new AOEShapeRect(60.0f, 40.0f));

sealed class LookUponMeAndDespairAOE(BossModule module) : Components.SimpleAOEs(module, (uint)AID.LookUponMeAndDespairAOE, new AOEShapeRect(100.0f, 8.0f))
{
    private WDir? facing;
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        base.OnCastStarted(caster, spell);
        if (spell.Action.ID == (uint)AID.LookUponMeAndDespairAOE)
            facing = spell.Rotation.ToDirection();
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        // Only the final middle cleave (between tether sets 9 and 10).
        if (NumCasts == 0 && Module.FindComponent<BlackHole>()?.NumCasts == 23 && facing is { } direction)
            Arena.ZoneCircleOutline(Arena.Center + direction.OrthoR() * 10f, 1f, Colors.Safe, 2f);
    }
}

sealed class WhiteHole(BossModule module) : Components.RaidwideCast(module, (uint)AID.WhiteHole);

sealed class EarthquakeRaidwide(BossModule module) : Components.RaidwideCast(module, (uint)AID.EarthquakeRaidwide);

sealed class BlackHoleActors(BossModule module) : Components.Voidzone(module, 2.0f, enemies => enemies.Enemies((uint)OID.BlackHole));

// Per-player progress comes from statuses, not the order in which players choose head markers.
sealed class KoreanP3BlackHoleAssignments
{
    public readonly int[] Order = new int[8];
    public readonly int[] Hits = new int[8]; // 0, 1, 2, or 3 (completed)
    public readonly bool[] HadCrust = new bool[8];
    public static int Round(int casts) => casts switch
    {
        0 => 0, 1 => 1, 3 => 2, 6 => 3, 9 => 4,
        12 => 5, 15 => 6, 18 => 7, 21 => 8, 23 => 9, _ => -1
    };

    // Sorted progress for First/Second/Third in Line before each set.
    // This also prevents assignments from using half-applied status updates.
    private static readonly int[][] Expected =
    [
        [0,0,0, 0,0,0, 0,0],
        [0,0,1, 0,0,0, 0,0],
        [0,1,2, 0,0,0, 0,0],
        [1,2,3, 0,0,0, 0,0],
        [2,3,3, 0,0,1, 0,0],
        [3,3,3, 0,1,2, 0,0],
        [3,3,3, 1,2,3, 0,0],
        [3,3,3, 2,3,3, 0,1],
        [3,3,3, 3,3,3, 1,2],
        [3,3,3, 3,3,3, 2,3]
    ];
    private static readonly (int Order, int Hits)[][] Plans =
    [
        [(1,0)],
        [(1,0),(1,0)], // The same attack-1 player takes BOTH lines.
        [(1,2),(1,1),(1,0)],
        [(2,0),(1,2),(1,1)],
        [(2,1),(2,0),(1,2)],
        [(2,2),(2,1),(2,0)],
        [(3,0),(2,2),(2,1)],
        [(3,1),(3,0),(2,2)],
        [(3,1),(3,2)], // Clockwise hole: stop-2; counterclockwise hole: stop-1.
        [(3,2)]
    ];

    public int[] Candidates(int casts)
    {
        var round = Round(casts);
        if (round < 0)
            return [];
        var progress = new List<int>(8);
        for (var group = 1; group <= 3; ++group)
        {
            var groupHits = Enumerable.Range(0, 8).Where(i => Order[i] == group).Select(i => Hits[i]).Order().ToArray();
            if (groupHits.Length != (group == 3 ? 2 : 3))
                return [];
            progress.AddRange(groupHits);
        }
        if (!progress.SequenceEqual(Expected[round]))
            return [];
        return Plans[round].Select(p => Enumerable.Range(0, 8)
            .Where(i => Order[i] == p.Order && Hits[i] == p.Hits)
            .Aggregate(0, (mask, i) => mask | (1 << i))).ToArray();
    }
}

sealed class BlackHole(BossModule module) : Components.BaitAwayTethers(module, new AOEShapeRect(125.0f, 3.0f), (uint)TetherID.BlackHoleTether)
{
    private readonly List<(Actor blackHole, ulong target)> Tethers = [];
    private readonly KefkaMax? kefkaMax = module.FindComponent<KefkaMax>();
    private readonly KoreanP3BlackHoleAssignments assignments = new();
    private readonly Dictionary<ulong, int> candidatesBySource = [];
    private readonly HashSet<ulong> firedSources = [];
    private int preparedAt = -1;

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        base.OnTethered(source, tether);
        if (tether.ID == (uint)TetherID.BlackHoleTether)
        {
            Tethers.RemoveAll(t => t.blackHole.InstanceID == source.InstanceID);
            Tethers.Add((source, tether.Target));
            firedSources.Remove(source.InstanceID);
        }
    }

    public override void OnUntethered(Actor source, in ActorTetherInfo tether)
    {
        base.OnUntethered(source, tether);
        if (tether.ID == (uint)TetherID.BlackHoleTether)
            Tethers.RemoveAll(t => t.blackHole.InstanceID == source.InstanceID);
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        var slot = Raid.FindSlot(actor.InstanceID);
        if (slot < 0 || slot >= 8)
            return;
        switch (status.ID)
        {
            case (uint)SID.FirstInLine: assignments.Order[slot] = 1; break;
            case (uint)SID.SecondInLine: assignments.Order[slot] = 2; break;
            case (uint)SID.ThirdInLine: assignments.Order[slot] = 3; break;
            case (uint)SID.PrimordialCrust: assignments.HadCrust[slot] = true; break;
            case (uint)SID._Gen_Unbecoming:
                assignments.Hits[slot] = Math.Max(assignments.Hits[slot], 1);
                break;
            case (uint)SID._Gen_MeanestExistence:
                assignments.Hits[slot] = Math.Max(assignments.Hits[slot], 2);
                break;
        }
    }

    public override void OnStatusLose(Actor actor, ref ActorStatus status)
    {
        var slot = Raid.FindSlot(actor.InstanceID);
        if (slot >= 0 && slot < 8 && status.ID == (uint)SID.PrimordialCrust && assignments.HadCrust[slot])
            assignments.Hits[slot] = 3;
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Nothingness)
        {
            ++NumCasts; // Preserve DMUStates' count of individual line attacks.
            firedSources.Add(caster.InstanceID);
            candidatesBySource.Clear();
            preparedAt = -1;
        }
    }

    private void PrepareAssignments()
    {
        if (preparedAt == NumCasts || kefkaMax?.boss == null)
            return;
        var candidates = assignments.Candidates(NumCasts);
        var pending = Tethers.Where(t => !firedSources.Contains(t.blackHole.InstanceID)).ToList();
        if (candidates.Length == 0 || pending.Count != candidates.Length)
            return;
        var boss = kefkaMax.boss;
        var north = boss.Position - boss.Rotation.ToDirection() * 20f - Arena.Center;
        if (north.LengthSq() < 0.01f)
            return;
        var start = MathF.Atan2(north.X, -north.Z) - 5f * MathF.PI / 180f;
        float ClockwiseAngle(Actor hole)
        {
            var delta = hole.Position - Arena.Center;
            var angle = MathF.Atan2(delta.X, -delta.Z) - start;
            return (angle % (2f * MathF.PI) + 2f * MathF.PI) % (2f * MathF.PI);
        }
        pending.Sort((a, b) => ClockwiseAngle(a.blackHole).CompareTo(ClockwiseAngle(b.blackHole)));
        candidatesBySource.Clear();
        for (var i = 0; i < pending.Count; ++i)
            candidatesBySource[pending[i].blackHole.InstanceID] = candidates[i];
        preparedAt = NumCasts; // Keep source identity stable through tether transfers.
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        base.AddHints(slot, actor, hints);
        if (slot < 0 || slot >= 8 || assignments.Hits[slot] == 3)
            return;
        PrepareAssignments();
        if (candidatesBySource.Values.Any(mask => (mask & (1 << slot)) != 0 && System.Numerics.BitOperations.PopCount((uint)mask) > 1))
            hints.Add("줄 후보: 머리징 순서에 맞는 사람이 처리", false);
    }

    public override PlayerPriority CalcPriority(int pcSlot, Actor pc, int playerSlot, Actor player, ref uint customColor)
    {
        foreach (var bait in ActiveBaitsOn(pc))
        {
            var currentBait = bait;
            if (IsClippedBy(player, ref currentBait))
            {
                customColor = Colors.Danger;
                return PlayerPriority.Danger;
            }
        }
        return base.CalcPriority(pcSlot, pc, playerSlot, player, ref customColor);
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        DrawTethers = false;
        base.DrawArenaForeground(pcSlot, pc);
        if (pcSlot < 0 || pcSlot >= 8)
            return;
        PrepareAssignments();
        foreach (var (hole, targetID) in Tethers)
        {
            var target = WorldState.Actors.Find(targetID);
            if (target == null || firedSources.Contains(hole.InstanceID))
                continue;
            var color = Colors.Danger;
            if (assignments.Hits[pcSlot] != 3 && candidatesBySource.TryGetValue(hole.InstanceID, out var mask) && (mask & (1 << pcSlot)) != 0)
                color = System.Numerics.BitOperations.PopCount((uint)mask) == 1 ? Colors.Safe : Colors.Object;
            Arena.AddLine(hole.Position, target.Position, color, 3f);
        }
    }
}


sealed class P3BlizzardBaits(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BlizzardIIIBaitCast, new AOEShapeCircle(6.0f))
{
    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.BlizzardIIIBaitCast)
        {
            NumCasts++;
        }
    }
}

sealed class P3Blizzard(DMU module) : Components.GenericBaitAway(module, centerAtTarget: true, onlyShowOutlines: true)
{
    private Actor? boss = null;
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();
    private readonly Actor kefkaBoss = module.BossP3()!;
    private WDir? facing;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.BlizzardIIICast)
        {
            boss = caster;
            facing = kefkaBoss.Rotation.ToDirection();
        }

        if (spell.Action.ID == (uint)AID.BlizzardIIIBaitCast)
        {
            NumCasts++;

            if (NumCasts == 16)
            {
                boss = null;
            }
        }
    }

    public override void Update()
    {
        CurrentBaits.Clear();

        if (boss == null)
        {
            return;
        }

        foreach (var player in Raid.WithoutSlot())
        {
            CurrentBaits.Add(new(boss, player, new AOEShapeCircle(6.0f)));
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        if (NumCasts >= 16 || pcSlot < 0 || pcSlot >= 8 || facing is not { } direction || partyConfig.SlotsPerAssignment(Raid).Length == 0)
            return;
        var role = partyConfig[Raid.Members[pcSlot].ContentId];
        var next = KoreanP3Positions.Blizzard(Arena.Center, direction, role);
        if (NumCasts < 8)
            Arena.ZoneCircleOutline(Arena.Center, 1f, Colors.Safe, 2f);
        Arena.ZoneCircleOutline(next, 1f, NumCasts < 8 ? Colors.Danger : Colors.Safe, 2f);
    }
}

sealed class P3BlizzardMove(BossModule module) : Components.StayMove(module, 5d)
{
    public int NumCasts = 0;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.BlizzardIIIRaidwide)
        {
            foreach (var (slot, _) in Raid.WithSlot(true, true, true))
            {
                PlayerStates[slot] = new(Requirement.Move, WorldState.FutureTime(spell.RemainingTime));
            }
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.BlizzardIIIRaidwide)
        {
            Array.Clear(PlayerStates);
            ++NumCasts;
        }
    }
}

sealed class KnockDown(BossModule module) : Components.GenericStackSpread(module)
{
    public List<WPos> stackLocations = [];
    public Class stackClass = Class.None;

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.StackShare)
        {
            var target = WorldState.Actors.Find(targetID);
            if (target == null)
            {
                return;
            }

            stackClass = target.Class;

            BitMask allowedPlayers = default;
            if (target.Class.IsSupport())
            {
                foreach (var (slot, player) in Raid.WithSlot(true, true, true))
                {
                    if (player.Class.IsSupport())
                    {
                        allowedPlayers.Set(slot);
                    }
                }
            }

            if (target.Class.IsDD())
            {
                foreach (var (slot, player) in Raid.WithSlot(true, true, true))
                {
                    if (player.Class.IsDD())
                    {
                        allowedPlayers.Set(slot);
                    }
                }
            }

            Stacks.Add(new(target, 6.0f, 4, 4, forbiddenPlayers: ~allowedPlayers));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.KnockDown)
        {
            if (Stacks.Count > 0)
            {
                stackLocations.Add(Stacks[0].Target.Position);
                Stacks.RemoveAt(0);
            }
        }
    }
    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        // Wait until the second set of ice circles has locked at player positions.
        if (Module.FindComponent<P3Blizzard>() is { NumCasts: < 16 })
            return;
        if (Stacks.Any(stack => stack.Target.Class.IsSupport() == pc.Class.IsSupport()))
            Arena.ZoneCircleOutline(Arena.Center, 1f, Colors.Safe, 2f);
    }
}

sealed class BigBang(BossModule module) : Components.GenericAOEs(module)
{
    private readonly KnockDown? stacks = module.FindComponent<KnockDown>();
    private readonly List<AOEInstance> aoes = [];
    private bool active = false;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.BigBangCast)
        {
            active = true;
        }
    }

    public override void Update()
    {
        aoes.Clear();

        if (!active)
        {
            return;
        }

        if (stacks != null && stacks.stackLocations.Count > 0)
        {
            foreach (var stack in stacks.stackLocations)
            {
                aoes.Add(new(new AOEShapeCircle(6.0f), stack));
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.BigBang)
        {
            NumCasts++;
            if (stacks != null)
            {
                if (stacks.stackLocations.Count > 0)
                {
                    stacks.stackLocations.RemoveAt(0);
                }
            }
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        return CollectionsMarshal.AsSpan(aoes);
    }
}

sealed class StompAMole(BossModule module) : Components.GenericTowers(module)
{
    private Actor? boss = null;
    private readonly KnockDown? stacks = module.FindComponent<KnockDown>();
    private enum TowerSide { LEFT, RIGHT }
    private readonly List<(Tower tower, TowerSide side, int wave)> towers = [];
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();

    private IEnumerable<(Tower tower, TowerSide side, int wave)> CurrentTowers => towers.Where(t => t.wave == (NumCasts < 2 ? 0 : 1));

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.StompAMoleCast)
        {
            boss = caster;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.StompAMoleTower)
        {
            var wave = NumCasts < 2 ? 0 : 1;
            towers.RemoveAll(t => t.wave == wave && t.tower.Position.AlmostEqual(caster.Position, 1.0f));
            NumCasts++;
        }
    }

    public override void Update()
    {
        if (boss == null)
        {
            return;
        }

        if (stacks == null || stacks.stackClass == Class.None)
        {
            return;
        }

        BitMask towerSoakers = default;

        if (stacks.stackClass.IsSupport())
        {
            foreach (var (slot, player) in Raid.WithSlot(true, true, true))
            {
                if (player.Class.IsDD())
                {
                    towerSoakers.Set(slot);
                }
            }
        }

        if (stacks.stackClass.IsDD())
        {
            foreach (var (slot, player) in Raid.WithSlot(true, true, true))
            {
                if (player.Class.IsSupport())
                {
                    towerSoakers.Set(slot);
                }
            }
        }

        towers.Add(new(new Tower(boss.Position + boss.Rotation.ToDirection().OrthoL() * 10.0f, 5.0f, 2, 2, forbiddenSoakers: ~towerSoakers), TowerSide.RIGHT, 0));
        towers.Add(new(new Tower(boss.Position + boss.Rotation.ToDirection().OrthoR() * 10.0f, 5.0f, 2, 2, forbiddenSoakers: ~towerSoakers), TowerSide.LEFT, 0));
        towers.Add(new(new Tower(boss.Position + boss.Rotation.ToDirection().OrthoL() * 10.0f, 5.0f, 2, 2, forbiddenSoakers: towerSoakers), TowerSide.RIGHT, 1));
        towers.Add(new(new Tower(boss.Position + boss.Rotation.ToDirection().OrthoR() * 10.0f, 5.0f, 2, 2, forbiddenSoakers: towerSoakers), TowerSide.LEFT, 1));
        boss = null; // Prevents towers constantly getting added
    }

    public override void DrawArenaBackground(int pcSlot, Actor pc)
    {
        foreach (var (tower, _, _) in CurrentTowers)
        {
            if (tower.ForbiddenSoakers[pcSlot] || !tower.IsInside(pc) && tower.NumInside(Module) >= tower.MaxSoakers)
            {
                tower.Shape.Draw(Arena, tower.Position, tower.Rotation);
            }
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        TowerSide? assignmentTower = null;
        var slots = partyConfig.SlotsPerAssignment(Raid);
        if (slots.Length > 0)
        {
            var assignment = partyConfig[Raid.Members[pcSlot].ContentId];
            if (assignment is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.H1 or
                PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.R1)
            {
                assignmentTower = TowerSide.LEFT;
            }
            else
            {
                assignmentTower = TowerSide.RIGHT;
            }
        }

        foreach (var (tower, side, _) in CurrentTowers)
        {
            if (tower.ForbiddenSoakers[pcSlot])
            {
                continue;
            }

            if (slots.Length > 0 && assignmentTower != side)
            {
                if (tower.NumInside(Module) < tower.MaxSoakers)
                {
                    tower.Shape.Outline(Arena, tower.Position, tower.Rotation, Colors.Danger, 2f);
                }
                continue;
            }

            var isInside = tower.IsInside(pc);
            var numInside = tower.NumInside(Module);
            var safe = numInside < tower.MaxSoakers || isInside && numInside <= tower.MaxSoakers;

            if (safe)
            {
                tower.Shape.Outline(Arena, tower.Position, tower.Rotation, Colors.Safe, 2f);
            }
            else if (isInside && numInside > tower.MaxSoakers)
            {
                tower.Shape.Outline(Arena, tower.Position, tower.Rotation, default, 2f);
            }
        }
    }
}

sealed class P3Enrage(BossModule module) : BossComponent(module)
{
    public bool enrage;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is ((uint)AID.BowelsOfAgonyEnrage) or ((uint)AID.MeteorEnrage))
        {
            enrage = true;
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is ((uint)AID.BowelsOfAgonyEnrage) or ((uint)AID.MeteorEnrage))
        {
            enrage = false;
        }
    }
}
