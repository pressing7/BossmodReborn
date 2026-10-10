namespace BossMod.Dawntrail.Ultimate.DMU;

// Fixed arena north is 0 degrees; positive angles below run clockwise.
static class KoreanP5Positions
{
    public static WPos At(WPos center, float degrees, float radius)
    {
        var radians = degrees * (MathF.PI / 180f);
        return center + new WDir(MathF.Sin(radians), -MathF.Cos(radians)) * radius;
    }

    public static float OrchestraAngle(PartyRolesConfig.Assignment assignment) => assignment switch
    {
        PartyRolesConfig.Assignment.OT => 22.5f,
        PartyRolesConfig.Assignment.R2 => 67.5f,
        PartyRolesConfig.Assignment.M2 => 112.5f,
        PartyRolesConfig.Assignment.H2 => 157.5f,
        PartyRolesConfig.Assignment.H1 => 202.5f,
        PartyRolesConfig.Assignment.M1 => 247.5f,
        PartyRolesConfig.Assignment.R1 => 292.5f,
        PartyRolesConfig.Assignment.MT => 337.5f,
        _ => float.NaN
    };

    public static WPos? TankDiffusion(WPos center, Actor actor)
    {
        if (actor.FindStatus((uint)SID.SurpriseFlare) != null)
            return At(center, 0f, 19f);
        if (actor.FindStatus((uint)SID.SurpriseHoly) != null)
            return At(center, 0f, 10f);
        return null;
    }
}

sealed class UltimaRepeater(BossModule module) : Components.RaidwideCast(module, (uint)AID.UltimaRepeaterCast)
{
    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.UltimaRepeaterRaidwide)
        {
            ++NumCasts;
        }
    }
}

sealed class FellForces(DMU module) : Components.GenericBaitStack(module)
{
    public bool active = false;
    private bool setup = false;
    public int expectedCasts = 9;
    private readonly Actor boss = module.KefkaP5()!;

    public override void Update()
    {
        if (!active || setup)
        {
            return;
        }

        SetupBaits();
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.FellForces) or ((uint)AID.FellForces1) or ((uint)AID.FellForces2))
        {
            ++NumCasts;

            if (NumCasts == expectedCasts)
            {
                CurrentBaits.Clear();
            }
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        if (!active || NumCasts >= expectedCasts)
            return;
        var angle = pc.Role switch
        {
            Role.Tank => 0f,
            Role.Healer => 225f,
            Role.Melee or Role.Ranged => 135f,
            _ => float.NaN
        };
        if (!float.IsNaN(angle))
            Arena.ZoneCircleOutline(KoreanP5Positions.At(Arena.Center, angle, 8f), 1f, Colors.Safe, 2f);
    }

    private void SetupBaits()
    {
        var party = Raid.WithSlot(true, true, true);
        BitMask allowedTanks = default;
        BitMask allowedHealers = default;
        BitMask allowedDDs = default;

        var len = party.Length;
        for (var i = 0; i < len; ++i)
        {
            ref var p = ref party[i];

            if (p.Item2.Role == Role.Tank)
            {
                allowedTanks.Set(p.Item1);
            }
            else if (p.Item2.Role == Role.Healer)
            {
                allowedHealers.Set(p.Item1);
            }
            else if (p.Item2.Role is Role.Melee or Role.Ranged)
            {
                allowedDDs.Set(p.Item1);
            }
        }

        var addedTank = false;
        var addedHealer = false;
        var addedDD = false;

        for (var i = 0; i < len; ++i)
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
                    CurrentBaits.Add(new(p, boss, new AOEShapeCircle(3f), forbidden: ~allowedTanks));
                    addedTank = true;
                }
            }
            else if (p.Role == Role.Healer)
            {
                if (!addedHealer)
                {
                    CurrentBaits.Add(new(p, boss, new AOEShapeCircle(5f), forbidden: ~allowedHealers));
                    addedHealer = true;
                }
            }
            else if (p.Role is Role.Melee or Role.Ranged)
            {
                if (!addedDD)
                {
                    CurrentBaits.Add(new(p, boss, new AOEShapeCircle(5f), forbidden: ~allowedDDs));
                    addedDD = true;
                }
            }
        }
        setup = true;
    }
}

sealed class ChaoticFlood(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ChaoticFloodAOE, new AOEShapeRect(40.0f, 5.0f))
{
    // These are stored here since many different strategies use different waymarks, but these should work for all
    private readonly WPos waymarkA = new(100.0f, 88.0f);
    private readonly WPos waymarkB = new(112.0f, 100.0f);
    private readonly WPos waymarkC = new(100.0f, 112.0f);
    private readonly WPos waymarkD = new(88.0f, 100.0f);

    private readonly List<AOEInstance> aoes = [];
    private readonly List<AOEInstance> aoesHints = []; // Used for when the aoes are first cast as hints
    private int aoesHintsDisplayed = 0;

    private readonly List<WPos> safeWaymarks = [];
    private bool? clockwise = null;
    private bool firstWaveResolved = false;
    private bool secondWaveResolved = false;
    private WDir currentDirection;
    private int castsResolved = 0;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFloodAOEDisplay)
        {
            aoes.Add(new(new AOEShapeRect(20.0f, 5.0f, 20.0f), caster.Position, caster.Rotation, actorID: caster.InstanceID));
            aoesHints.Add(new(new AOEShapeRect(20.0f, 5.0f, 20.0f), caster.Position, caster.Rotation, color: Colors.Enemy, actorID: caster.InstanceID));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFloodAOEDisplay)
        {
            aoesHintsDisplayed++;
            aoesHints.RemoveAt(0);
        }

        if (spell.Action.ID == (uint)AID.ChaoticFloodAOE)
        {
            if (++castsResolved == 2)
            {
                castsResolved = 0;
                if (clockwise != null)
                {
                    currentDirection = clockwise.Value ? new WDir(-currentDirection.Z, currentDirection.X) : new WDir(currentDirection.Z, -currentDirection.X);
                }
            }

            NumCasts++;
            aoes.RemoveAt(0);
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (!secondWaveResolved || clockwise == null || NumCasts == 8)
        {
            return;
        }

        Arena.ZoneCircleOutline(Module.Center + currentDirection * 2.0f, 1.0f, Colors.Safe, 2.0f);

        if (NumCasts <= 4)
        {
            var nextDirection = clockwise.Value ? new WDir(-currentDirection.Z, currentDirection.X) : new WDir(currentDirection.Z, -currentDirection.X);
            Arena.ZoneCircleOutline(Module.Center + nextDirection * 2.0f, 1.0f, Colors.Danger, 2.0f);
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        if (aoesHintsDisplayed < 8)
        {
            return CollectionsMarshal.AsSpan(aoesHints.Take(2).ToList());
        }

        return CollectionsMarshal.AsSpan(aoes.Take(4).ToList());
    }

    public override void Update()
    {
        if (!firstWaveResolved && aoes.Count >= 2)
        {
            List<WPos> waymarks = [waymarkA, waymarkB, waymarkC, waymarkD];
            var aoesFirstWave = aoes.Take(2).ToList();

            foreach (var waymark in waymarks)
            {
                if (!aoesFirstWave.Any(aoe => aoe.Check(waymark)))
                {
                    safeWaymarks.Add(waymark);
                }
            }

            firstWaveResolved = true;
        }

        if (firstWaveResolved && !secondWaveResolved && aoes.Count >= 4)
        {
            var aoesSecondWave = aoes.Skip(2).Take(2).ToList();
            foreach (var waymark in safeWaymarks)
            {
                if (!aoesSecondWave.Any(aoe => aoe.Check(waymark)))
                {
                    var otherWaymark = safeWaymarks.First(point => point != waymark) - Module.Center;
                    clockwise = otherWaymark.X * (waymark - Module.Center).Z - otherWaymark.Z * (waymark - Module.Center).X > 0;
                    currentDirection = (waymark - Module.Center).Normalized();
                    break;
                }
            }
            secondWaveResolved = true;
        }
    }
}

sealed class ChaoticFloodStack(BossModule module) : Components.GenericStackSpread(module)
{
    public int NumCasts = 0;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFlood)
        {
            var target = WorldState.Actors.Find(caster.TargetID);
            if (target == null)
            {
                return;
            }
            Stacks.Add(new(target, 6.0f, 8, 8));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFloodStack)
        {
            NumCasts++;

            if (NumCasts == 4)
            {
                Stacks.Clear();
            }
        }
    }
}

sealed class MaddeningOrchestra(DMU module) : Components.GenericBaitAway(module, centerAtTarget: true)
{
    private bool active = false;
    private bool tankStackResolved;
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();
    private readonly DateTime[] magicVulnerability = new DateTime[PartyState.MaxPartySize];
    private bool firstWave = false;
    private readonly Actor boss = module.KefkaP5()!;

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.MagicVulnerabilityUp)
        {
            var slot = Raid.FindSlot(actor.InstanceID);
            if (slot >= 0)
            {
                magicVulnerability[slot] = status.ExpireAt;
            }
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.MaddeningOrchestra)
        {
            active = true;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFlareTB)
            tankStackResolved = true;
        if (spell.Action.ID is ((uint)AID.Holy) or ((uint)AID.Flare))
        {
            ++NumCasts;

            if (NumCasts >= 5)
            {
                firstWave = true;
            }
        }
    }

    public override void Update()
    {
        CurrentBaits.Clear();

        if (!active || NumCasts > 5)
        {
            return;
        }

        var targets = Raid.WithoutSlot(true, true, true).SortedByRange(boss.Position).ToList();
        if (!firstWave)
        {
            foreach (var player in targets)
            { // Tanks & rest of party are hit with different spells, but same size AOEs
                CurrentBaits.Add(new(boss.Position, player, new AOEShapeCircle(5.0f)));
            }

            return;
        }

        targets = [.. targets.Take(3)];
        BitMask forbiddenPlayers = Raid.WithSlot(true, true, true).Where(p => magicVulnerability[p.Item1] > WorldState.CurrentTime || p.Item2.Role == Role.Tank).Mask();
        foreach (var target in targets)
        {
            CurrentBaits.Add(new(boss.Position, target, new AOEShapeCircle(5.0f), forbidden: forbiddenPlayers));
        }

        ForbiddenPlayers = forbiddenPlayers;
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        if (!active || pcSlot < 0 || pcSlot >= PartyState.MaxPartySize)
            return;

        WPos? position;
        if (pc.Role == Role.Tank && (firstWave || tankStackResolved))
            position = tankStackResolved ? KoreanP5Positions.TankDiffusion(Arena.Center, pc) : KoreanP5Positions.At(Arena.Center, 0f, 10f);
        else if (pc.Role != Role.Tank && NumCasts > 5)
            position = KoreanP5Positions.At(Arena.Center, 180f, 11f);
        else
        {
            if (partyConfig.SlotsPerAssignment(Raid).Length == 0)
                return;
            var angle = KoreanP5Positions.OrchestraAngle(partyConfig[Raid.Members[pcSlot].ContentId]);
            if (float.IsNaN(angle))
                return;
            // Wait for all three first-wave non-tank vulnerabilities to arrive.
            // This prevents briefly sending all six players inward between events.
            var vulnerabilitiesReady = Raid.WithSlot(true, true, true)
                .Count(p => p.Item2.Role != Role.Tank && magicVulnerability[p.Item1] > WorldState.CurrentTime) == 3;
            var bait = firstWave && vulnerabilitiesReady && pc.Role != Role.Tank && magicVulnerability[pcSlot] <= WorldState.CurrentTime;
            position = KoreanP5Positions.At(Arena.Center, angle, bait ? 7f : 11f);
        }
        if (position is WPos safe)
            Arena.ZoneCircleOutline(safe, 1f, Colors.Safe, 2f);
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        base.AddHints(slot, actor, hints);

        if (active && NumCasts == 5 && !ForbiddenPlayers[slot] && firstWave)
        {
            if (IsBaitTarget(actor))
            {
                hints.Add("Bait!", false);
            }
            else
            {
                hints.Add("Bait!");
            }
        }
    }
}

sealed class ChaoticFlareTB(DMU module) : Components.GenericBaitStack(module, (uint)AID.ChaoticFlareTB)
{
    public bool active = false;
    private readonly Actor boss = module.KefkaP5()!;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFlareTB)
        {
            NumCasts++;
            active = false;
            CurrentBaits.Clear();
        }
    }

    public override void Update()
    {
        if (!active)
        {
            return;
        }

        CurrentBaits.Clear();

        var party = Raid.WithSlot(true, true, true);
        BitMask allowedTanks = default;
        var len = party.Length;
        for (var i = 0; i < len; ++i)
        {
            ref var p = ref party[i];

            if (p.Item2.Role == Role.Tank)
            {
                allowedTanks.Set(p.Item1);
            }
        }

        var addedTank = false;

        for (var i = 0; i < len; ++i)
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
                    CurrentBaits.Add(new(p, boss, new AOEShapeCircle(5.0f), forbidden: ~allowedTanks));
                    addedTank = true;
                }
            }
        }
    }
}

sealed class ChaoticHolyFlareDiffusion(BossModule module) : Components.GenericBaitAway(module, centerAtTarget: true, onlyShowOutlines: true)
{
    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        if (NumCasts >= 2)
            return;
        var position = pc.Role == Role.Tank
            ? KoreanP5Positions.TankDiffusion(Arena.Center, pc)
            : KoreanP5Positions.At(Arena.Center, 180f, 11f);
        if (position is WPos safe)
            Arena.ZoneCircleOutline(safe, 1f, Colors.Safe, 2f);
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID is var id && id == (uint)SID.SurpriseHoly)
        {
            CurrentBaits.Add(new(actor, actor, new AOEShapeCircle(6.0f), status.ExpireAt));
        }
        else if (id == (uint)SID.SurpriseFlare)
        {
            CurrentBaits.Add(new(actor, actor, new AOEShapeCircle(25.0f), status.ExpireAt));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.FlareDiffusion) or ((uint)AID.ChaoticHoly))
        {
            NumCasts++;

            if (NumCasts == 2)
            {
                CurrentBaits.Clear();
            }
        }
    }
}

// Remember the initial debuffs, and lock assignments separately for each group of four glows.
// New glows and old explosions can be interleaved in the same update.
sealed class Celestriad(BossModule module) : Components.GenericTowers(module)
{
    private enum Elements { NONE, ICE, FIRE, THUNDER, NO_ELEMENT }
    private readonly List<(Actor actor, Elements element)> allTowers = [];
    private readonly List<Elements> towerOrder = [];
    private readonly Elements[] debuffs = Utils.MakeArray(PartyState.MaxPartySize, Elements.NONE);
    private readonly List<List<ulong>> waves = [];
    private readonly List<int> activeWaves = []; // Parallel to Towers.
    private readonly Dictionary<(int wave, ulong actorID), BitMask> assignments = [];
    private readonly HashSet<int> assignedWaves = [];
    private int numGlows;

    private float ClockwiseAngle(Actor actor)
    {
        var offset = actor.Position - Arena.Center;
        var angle = MathF.Atan2(offset.X, -offset.Z);
        return angle < 0f ? angle + 2f * MathF.PI : angle;
    }

    public override void OnActorCreated(Actor actor)
    {
        var element = actor.OID switch
        {
            (uint)OID.IceTower => Elements.ICE,
            (uint)OID.FireTower => Elements.FIRE,
            (uint)OID.ThunderTower => Elements.THUNDER,
            _ => Elements.NONE
        };
        if (element == Elements.NONE || allTowers.Any(t => t.actor.InstanceID == actor.InstanceID))
            return;
        allTowers.Add((actor, element));
        if (allTowers.Count != 9)
            return;

        allTowers.Sort((a, b) => ClockwiseAngle(a.actor).CompareTo(ClockwiseAngle(b.actor)));
        // Start at an element boundary, so a group straddling north is not split.
        var start = allTowers.FindIndex(t => t.element != allTowers[8].element);
        if (start < 0)
            return;
        var ordered = allTowers.Skip(start).Concat(allTowers.Take(start)).ToArray();
        allTowers.Clear();
        allTowers.AddRange(ordered);
        towerOrder.Clear();
        for (var i = 0; i < 9; i += 3)
        {
            var elementAtStart = allTowers[i].element;
            if (allTowers[i + 1].element != elementAtStart || allTowers[i + 2].element != elementAtStart || towerOrder.Contains(elementAtStart))
            {
                towerOrder.Clear();
                return;
            }
            towerOrder.Add(elementAtStart);
        }
        Update();
    }

    public override void OnActorEAnim(Actor actor, uint state)
    {
        if (actor.OID is not ((uint)OID.IceTower) and not ((uint)OID.FireTower) and not ((uint)OID.ThunderTower))
            return;
        if (state == (uint)Animations.TowerGlow)
        {
            var wave = numGlows / 4;
            if (wave >= 3)
                return;
            if (waves.Count == wave)
                waves.Add([]);
            if (waves[wave].Contains(actor.InstanceID))
                return;
            waves[wave].Add(actor.InstanceID);
            ++numGlows;
            activeWaves.Add(wave);
            Towers.Add(new(actor.Position, 3f, 2, 2, forbiddenSoakers: new BitMask(0xFF), actorID: actor.InstanceID));
            Update();
        }
        else if (state == (uint)Animations.TowerExplosion)
        {
            // Remove the oldest activation of this actor, not a newly glowing tower.
            var index = Towers.FindIndex(t => t.ActorID == actor.InstanceID);
            if (index >= 0)
            {
                ++NumCasts;
                Towers.RemoveAt(index);
                activeWaves.RemoveAt(index);
            }
        }
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        var slot = Raid.FindSlot(actor.InstanceID);
        if (slot < 0 || debuffs[slot] != Elements.NONE)
            return;
        debuffs[slot] = status.ID switch
        {
            (uint)SID.IceResistanceDownII => Elements.ICE,
            (uint)SID.FireResistanceDownII => Elements.FIRE,
            (uint)SID.LightningResistanceDownII => Elements.THUNDER,
            _ => Elements.NONE
        };
        if (debuffs.Count(d => d != Elements.NONE) == 6)
        {
            for (var i = 0; i < debuffs.Length; ++i)
                if (debuffs[i] == Elements.NONE)
                    debuffs[i] = Elements.NO_ELEMENT;
        }
        Update();
    }

    public override void Update()
    {
        if (towerOrder.Count != 3 || debuffs.Contains(Elements.NONE))
            return;
        for (var wave = 0; wave < waves.Count; ++wave)
        {
            if (assignedWaves.Contains(wave) || waves[wave].Count != 4)
                continue;
            // allTowers already follows clockwise order within every element group.
            var lit = allTowers.Where(t => waves[wave].Contains(t.actor.InstanceID)).ToArray();
            if (lit.Length != 4)
                continue;
            var groups = lit.GroupBy(t => t.element).ToArray();
            if (groups.Length != 3 || groups.Count(g => g.Count() == 2) != 1)
                continue;
            var secondTower = groups.First(g => g.Count() == 2).Last().actor.InstanceID;
            foreach (var tower in lit)
            {
                BitMask forbidden = default;
                for (var slot = 0; slot < debuffs.Length; ++slot)
                {
                    var allowed = debuffs[slot] == Elements.NO_ELEMENT
                        ? tower.actor.InstanceID == secondTower
                        : tower.actor.InstanceID != secondTower && tower.element == towerOrder[(towerOrder.IndexOf(debuffs[slot]) + wave + 1) % 3];
                    if (!allowed)
                        forbidden.Set(slot);
                }
                assignments[(wave, tower.actor.InstanceID)] = forbidden;
            }
            assignedWaves.Add(wave);
        }
        for (var i = 0; i < Towers.Count; ++i)
        {
            var tower = Towers[i];
            if (assignments.TryGetValue((activeWaves[i], tower.ActorID), out var forbidden))
            {
                tower.ForbiddenSoakers = forbidden;
                Towers[i] = tower;
            }
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        if (pcSlot < 0 || pcSlot >= PartyState.MaxPartySize || activeWaves.Count == 0)
            return;
        var wave = activeWaves.Min();
        for (var i = 0; i < Towers.Count; ++i)
            if (activeWaves[i] == wave && !Towers[i].ForbiddenSoakers[pcSlot])
                Arena.ZoneCircleOutline(Towers[i].Position, 1f, Colors.Safe, 2f);
    }
}

sealed class CatastrophicChoice(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> aoes = [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.CatastrophicChoiceQuake)
        {
            aoes.Add(new(new AOEShapeCircle(10.0f), caster.Position));
        }

        if (spell.Action.ID == (uint)AID.CatastrophicChoiceTornado)
        {
            aoes.Add(new(new AOEShapeDonut(10.0f, 40.0f), caster.Position));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.Quake) or ((uint)AID.Tornado))
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
}

sealed class StrayApocalypse(BossModule module) : Components.Exaflare(module, 6f)
{
    private readonly List<AOEInstance> currentAOEs = [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.StrayApocalypseExaFlareCast)
        {
            Lines.Add(new(caster.Position, 7.071f * spell.Rotation.ToDirection(), Module.CastFinishAt(spell), 0.5f, 7, 7));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.StrayApocalypseExaFlareCast) or
            ((uint)AID.StrayApocalypseExaFlare))
        {
            ++NumCasts;
            var count = Lines.Count;
            var pos = caster.Position;

            for (var i = 0; i < count; ++i)
            {
                var line = Lines[i];
                if (line.Next.AlmostEqual(pos, 1f))
                {
                    AdvanceLine(line, pos);
                    if (line.ExplosionsLeft == 0)
                    {
                        Lines.RemoveAt(i);
                    }

                    return;
                }
            }
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        currentAOEs.Clear();

        var exaFlares = Lines.Take(4).ToList();
        for (var i = 0; i < exaFlares.Count; ++i)
        {
            var line = Lines[i];
            var pos = line.Next;
            var time = line.NextExplosion;

            for (var k = 0; k < line.ExplosionsLeft; ++k)
            {
                currentAOEs.Add(new(Shape, pos.Quantized(), line.Rotation, time, k == 0 ? ImminentColor : FutureColor));
                pos += line.Advance;
                time = time.AddSeconds(line.TimeToMove);
            }
        }

        return CollectionsMarshal.AsSpan(currentAOEs);
    }
}

sealed class StrayEntropy(BossModule module) : Components.UniformStackSpread(module, 0f, 5.0f)
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.StrayEntropyCast)
        {
            foreach (var p in Raid.WithoutSlot())
            {
                Spreads.Add(new(p, 5.0f));
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.StrayEntropySpread)
        {
            Spreads.Clear();
        }
    }
}

sealed class P5ForsakenRaidWide(BossModule module) : Components.RaidwideCast(module, (uint)AID.ForsakenCast);

// TODO update to use MapEffects maybe?
sealed class P5ForsakenGround(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ForsakenGround, new AOEShapeCircle(8.0f))
{
    private readonly List<AOEInstance> aoes = [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.ForsakenGround)
        {
            aoes.Add(new(new AOEShapeCircle(8.0f), caster.Position, actorID: caster.InstanceID));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ForsakenGround)
        {
            NumCasts++;
            if (aoes.Count > 0)
            {
                var aoeIndex = aoes.FindIndex(a => a.ActorID == caster.InstanceID && a.Color != Colors.Danger);
                if (aoeIndex >= 0)
                {
                    var aoe = aoes[aoeIndex];
                    aoe.Color = Colors.Danger;
                    aoes[aoeIndex] = aoe;
                }
            }
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        return CollectionsMarshal.AsSpan(aoes);
    }
}

// Follow actual puddle telegraphs; do not predict which player is selected.
sealed class P5ForsakenBait(DMU module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> aoes = [];
    private readonly HashSet<(ulong caster, DateTime finish)> seenCasts = [];
    private int step;
    private bool started;
    private bool finished;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.ForsakenCast)
            started = true;
        if (spell.Action.ID != (uint)AID.ForsakenAOEBait || !seenCasts.Add((caster.InstanceID, Module.CastFinishAt(spell))))
            return;
        started = true;
        step = Math.Min(step + 1, 4);
        aoes.Add(new(new AOEShapeCircle(8f), caster.Position, activation: Module.CastFinishAt(spell), actorID: caster.InstanceID));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ForsakenAOEBait)
        {
            var index = aoes.FindIndex(a => a.ActorID == caster.InstanceID);
            if (index >= 0)
            {
                aoes.RemoveAt(index);
                ++NumCasts;
            }
        }
        else if (spell.Action.ID == (uint)AID.ForsakenBonds && step == 4)
            finished = true;
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(aoes);

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        if (finished)
            return;
        // The existing timeline activates this component during the last auto attack.
        var forces = Module.FindComponent<FellForces>();
        if (!started && forces != null && forces.active && forces.NumCasts < forces.expectedCasts)
            return;
        // SW -> NW -> NE -> SE -> SW; every point is 11 yalms from the center.
        Arena.ZoneCircleOutline(KoreanP5Positions.At(Arena.Center, 225f + step * 90f, 11f), 1f, Colors.Safe, 2f);
    }
}

sealed class P5ForsakenStack(BossModule module) : Components.StackTogether(module, (uint)IconID.StackShare, 5.0f, 6.0f);


// TODO: refine the phase-entry timing in DMUStates.
