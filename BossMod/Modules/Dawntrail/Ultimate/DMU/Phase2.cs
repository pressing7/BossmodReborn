namespace BossMod.Dawntrail.Ultimate.DMU;

sealed class UltimateEmbrace(BossModule module) : Components.CastSharedTankbuster(module, (uint)AID.UltimateEmbrace, 5f);

sealed class Forsaken(BossModule module) : Components.RaidwideCast(module, (uint)AID.Forsaken);

sealed class LightOfJudgmentP2(BossModule module) : Components.RaidwideCast(module, (uint)AID.LightOfJudgmentP2);

// Used for towers' spawn locations and marking them as SW or SE depending on the spawn point.
sealed class PathOfLight(BossModule module) : Components.GenericTowers(module, (uint)AID.ThePathOfLight)
{
    public int CurrentSW = -1;
    public int CurrentSE = -1;

    public override void OnMapEffect(byte index, uint state)
    {
        if (index >= 0x01 && index <= 0x08 && state == 0x00020001u)
        {
            var angle = (180f - (index - 1) * 45f).Degrees();
            Towers.Add(new(Arena.Center + angle.ToDirection() * 8f, 4f, 2, 2, default, WorldState.FutureTime(10d)));
            UpdateCurrentTowers();
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            var count = Towers.Count;
            var towers = CollectionsMarshal.AsSpan(Towers);
            var pos = caster.Position;
            for (var i = 0; i < count; ++i)
            {
                if (towers[i].Position.AlmostEqual(pos, 1f))
                {
                    Towers.RemoveAt(i);
                    break;
                }
            }
            UpdateCurrentTowers();
        }
    }
    public override void Update() => UpdateCurrentTowers();
    public void UpdateCurrentTowers()
    {
        if (Towers.Count != 2)
        {
            return;
        }

        var towers = CollectionsMarshal.AsSpan(Towers);
        var tower1 = towers[0].Position;
        var tower2 = towers[1].Position;

        var middleOfTowers = new WPos((tower1.X + tower2.X) * 0.5f, (tower1.Z + tower2.Z) * 0.5f);
        var southDirection = (middleOfTowers - Arena.Center).Normalized();

        if ((tower1 - middleOfTowers).Dot(southDirection.OrthoL()) <= (tower2 - middleOfTowers).Dot(southDirection.OrthoL()))
        {
            CurrentSW = 0;
            CurrentSE = 1;
        }
        else
        {
            CurrentSW = 1;
            CurrentSE = 0;
        }
    }
}

// Used for setting up each player's role, such as the shape the player has, the pair the player belongs, if the player is a helper or soaker, etc.
sealed class ForsakenShapes(BossModule module) : BossComponent(module)
{
    private static readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();
    private static readonly DMUConfig dmuConfig = Service.Config.Get<DMUConfig>();
    public int currentTowerSet = 1; // We start on odd tower set
    private int pathOfLightCasts;

    public enum Shape { None, Spread, Cone, Stack }
    public Shape[] shapes = new Shape[8];
    public enum TowerRole { Unknown, Helper, Taker }

    public BitMask swSoakers;
    public BitMask seSoakers;
    public BitMask supportHelpers;
    public BitMask dpsHelpers;

    // TODO merge these together
    public bool pairsLocked;
    private bool pairsSwapped;

    public sealed class PairInfo(PartyRolesConfig.Assignment player1, PartyRolesConfig.Assignment player2, bool isSupport)
    {
        public PartyRolesConfig.Assignment player1Assignment = player1;
        public PartyRolesConfig.Assignment player2Assignment = player2;
        public bool isSupport = isSupport;
        public TowerRole role = TowerRole.Unknown;
    }

    public readonly PairInfo[] pairs = [
        new(PartyRolesConfig.Assignment.MT, PartyRolesConfig.Assignment.H1, true),
        new(PartyRolesConfig.Assignment.OT, PartyRolesConfig.Assignment.H2, true),
        new(PartyRolesConfig.Assignment.M1, PartyRolesConfig.Assignment.R1, false),
        new(PartyRolesConfig.Assignment.M2, PartyRolesConfig.Assignment.R2, false),
    ];

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != (uint)AID.ThePathOfLight)
        {
            return;
        }

        if (++pathOfLightCasts < 2)
        {
            return;
        }

        pathOfLightCasts = 0;
        ++currentTowerSet;

        if (currentTowerSet is 4 or 8)
        {
            pairsSwapped = false;
        }
    }

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        var shape = iconID switch
        {
            (uint)IconID.TowerSpreadIcon => Shape.Spread,
            (uint)IconID.TowerConeIcon => Shape.Cone,
            (uint)IconID.TowerStackIcon => Shape.Stack,
            _ => default
        };

        if (shape != default)
        {
            var slot = Raid.FindSlot(actor.InstanceID);
            if (slot >= 0)
            {
                shapes[slot] = shape;
                korean.ObserveIcon(slot, shape);
            }
        }
    }

    public override void Update()
    {
        swSoakers = default;
        seSoakers = default;
        supportHelpers = default;
        dpsHelpers = default;

        var slots = partyConfig.SlotsPerAssignment(Raid);
        if (slots.Length == 0)
        {
            return;
        }

        if (UseKorean)
        {
            UpdateKorean(slots);
            return;
        }

        SetupPairs(slots);

        if ((currentTowerSet == 4 || currentTowerSet == 8) && !pairsSwapped)
        {
            foreach (var pair in pairs)
            {
                pair.role = pair.role switch
                {
                    TowerRole.Helper => TowerRole.Taker,
                    TowerRole.Taker => TowerRole.Helper,
                    _ => pair.role
                };
            }

            pairsSwapped = true;
        }

        foreach (var pair in pairs)
        {
            var slotPlayer1 = slots[(int)pair.player1Assignment];
            var slotPlayer2 = slots[(int)pair.player2Assignment];
            var shapeA = shapes[slotPlayer1];
            var shapeB = shapes[slotPlayer2];

            if (pair.role == TowerRole.Helper)
            {
                if (pair.isSupport)
                {
                    supportHelpers.Set(slotPlayer1);
                    supportHelpers.Set(slotPlayer2);
                }
                else
                {
                    dpsHelpers.Set(slotPlayer1);
                    dpsHelpers.Set(slotPlayer2);
                }
            }

            if (pair.role == TowerRole.Taker && dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                // First set of towers (tower set odd)
                if ((currentTowerSet & 1) != 0)
                {
                    // Case: for the first set of towers no adjustment is needed between the Melee & Tank
                    if (currentTowerSet == 1)
                    {
                        if (shapeA == Shape.Cone || shapeB == Shape.Cone)
                        {
                            swSoakers.Set(slotPlayer1);
                            swSoakers.Set(slotPlayer2);
                        }

                        if (shapeA == Shape.Spread || shapeB == Shape.Spread)
                        {
                            seSoakers.Set(slotPlayer1);
                            seSoakers.Set(slotPlayer2);
                        }
                    }

                    // Case: every odd tower beyond the first set, the Melee & Tank may need to adjust base on pair shapes
                    if (currentTowerSet > 1)
                    {
                        // Cones and spreads are forced, where cone is always SW and spread is always SE
                        if (shapeA == Shape.Cone)
                        {
                            swSoakers.Set(slotPlayer1);
                        }

                        if (shapeB == Shape.Cone)
                        {
                            swSoakers.Set(slotPlayer2);
                        }

                        if (shapeA == Shape.Spread)
                        {
                            seSoakers.Set(slotPlayer1);
                        }

                        if (shapeB == Shape.Spread)
                        {
                            seSoakers.Set(slotPlayer2);
                        }

                        // If the pairs has the same shape, an adjustment is needed
                        if (shapeA == shapeB)
                        {
                            // If supports are the same shape, MT/OT has to go to the SE tower
                            if (pair.isSupport)
                            {
                                seSoakers.Set(slotPlayer1);
                                swSoakers.Set(slotPlayer2);
                            }

                            // If dps are the same shape, M1/M2 goes to the SW tower
                            if (!pair.isSupport)
                            {
                                swSoakers.Set(slotPlayer1);
                                seSoakers.Set(slotPlayer2);
                            }
                        }
                        else
                        { // Otherwise people just go to their default side
                            if (pair.isSupport)
                            {
                                if (shapeA == Shape.Stack)
                                {
                                    swSoakers.Set(slotPlayer1);
                                }

                                if (shapeB == Shape.Stack)
                                {
                                    swSoakers.Set(slotPlayer2);
                                }
                            }

                            if (!pair.isSupport)
                            {
                                if (shapeA == Shape.Stack)
                                {
                                    seSoakers.Set(slotPlayer1);
                                }

                                if (shapeB == Shape.Stack)
                                {
                                    seSoakers.Set(slotPlayer2);
                                }
                            }
                        }
                    }
                }

                // Second set of towers (tower set even)
                if ((currentTowerSet & 1) == 0)
                {
                    if (pair.isSupport)
                    {
                        // They have different shapes - both go to the same tower which is west tower
                        if (shapeA != shapeB)
                        {
                            swSoakers.Set(slotPlayer1);
                            swSoakers.Set(slotPlayer2);
                        }
                        else
                        { // healer goes to SW tower, tank goes to SE tower - player2 is healer, player1 is tank
                            swSoakers.Set(slotPlayer2);
                            seSoakers.Set(slotPlayer1);
                        }
                    }

                    if (!pair.isSupport)
                    {
                        if (shapeA != shapeB)
                        {
                            // They have different shapes - both go to the same tower which is east tower
                            seSoakers.Set(slotPlayer1);
                            seSoakers.Set(slotPlayer2);
                        }
                        else
                        {
                            // range goes to SE tower, melee goes to SE tower - player2 is range, player1 is melee
                            seSoakers.Set(slotPlayer2);
                            swSoakers.Set(slotPlayer1);
                        }
                    }
                }
            }


        }
    }

    // Kept in ForsakenShapes (which survives all eight rounds), never in a solver.
    private readonly ForsakenKoreanAssignments korean = new();
    public static bool UseKorean => dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Kroxy_Rinon_Melee_Flex;

    private void UpdateKorean(int[] slots)
    {
        if (!korean.Initialize(slots))
            return;
        pairsLocked = true;
        korean.Advance(currentTowerSet);
        if (currentTowerSet < 1 || currentTowerSet > 8)
            return;

        var group = ForsakenKoreanAssignments.ActiveGroup(currentTowerSet);
        foreach (var pair in pairs)
            pair.role = korean.Group(slots[(int)pair.player1Assignment]) == group ? TowerRole.Taker : TowerRole.Helper;

        var record = korean.Record(currentTowerSet);
        for (var slot = 0; slot < 8; ++slot)
        {
            if (korean.Group(slot) != group)
            {
                var role = korean.Assignment(slot);
                if (role is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2)
                    supportHelpers.Set(slot);
                else
                    dpsHelpers.Set(slot);
            }
            else if (record != null)
            {
                // Round 8 deliberately permits either tower; the player reads their sign.
                if (record.Sides[slot] is ForsakenKoreanAssignments.Side.SW or ForsakenKoreanAssignments.Side.Either)
                    swSoakers.Set(slot);
                if (record.Sides[slot] is ForsakenKoreanAssignments.Side.SE or ForsakenKoreanAssignments.Side.Either)
                    seSoakers.Set(slot);
            }
        }
    }

    public void UpdateKoreanTowerRestrictions(PathOfLight towers, bool odd)
    {
        if (((currentTowerSet & 1) != 0) != odd || towers.Towers.Count != 2)
            return;
        towers.UpdateCurrentTowers();
        var record = korean.Record(currentTowerSet);
        var span = CollectionsMarshal.AsSpan(towers.Towers);
        var party = new BitMask(0xFF);
        for (var i = 0; i < span.Length; ++i)
        {
            // Do not flag everyone as forbidden while a batch of icons is still arriving.
            span[i].ForbiddenSoakers = record == null ? default : party & ~(i == towers.CurrentSW ? swSoakers : seSoakers);
        }
    }

    public void DrawKoreanPositions(int slot, PathOfLight towers, bool odd, uint colour)
    {
        if (slot < 0 || slot >= 8 || currentTowerSet < 1 || currentTowerSet > 8 ||
            ((currentTowerSet & 1) != 0) != odd || towers.Towers.Count != 2)
            return;
        var record = korean.Record(currentTowerSet);
        if (record == null)
            return;
        towers.UpdateCurrentTowers();
        var sw = towers.Towers[towers.CurrentSW].Position;
        var se = towers.Towers[towers.CurrentSE].Position;
        var midpoint = new WPos((sw.X + se.X) * 0.5f, (sw.Z + se.Z) * 0.5f);
        var south = (midpoint - Arena.Center).Normalized();
        var east = south.OrthoL();
        var side = record.Sides[slot];

        if (side != ForsakenKoreanAssignments.Side.None)
        {
            var shape = record.Shapes[slot];
            // Separate ifs are intentional: round 8 draws both candidates.
            if (side is ForsakenKoreanAssignments.Side.SW or ForsakenKoreanAssignments.Side.Either)
                Arena.ZoneCircleOutline(KoreanTowerPosition(Arena.Center, sw, true, odd, shape), odd ? 1.0f : 0.75f, colour, 2.0f);
            if (side is ForsakenKoreanAssignments.Side.SE or ForsakenKoreanAssignments.Side.Either)
                Arena.ZoneCircleOutline(KoreanTowerPosition(Arena.Center, se, false, odd, shape), odd ? 1.0f : 0.75f, colour, 2.0f);
            return;
        }

        var bossPosition = (Module as DMU)?.BossP2()?.Position ?? Arena.Center;
        var position = KoreanHelperPosition(Arena.Center, sw, south, east, bossPosition, korean.Assignment(slot), odd);
        Arena.ZoneCircleOutline(position, odd ? 1.0f : 0.75f, colour, 2.0f);
    }

    internal static WPos KoreanTowerPosition(WPos center, WPos tower, bool sw, bool odd, Shape shape)
    {
        var inward = (center - tower).Normalized();
        if (odd)
            return shape == Shape.Stack ? (sw ? tower : tower + inward * 2.0f + inward.OrthoL() * 0.5f) : tower - inward * 3.8f;
        var offset = inward.OrthoL() * (sw ? 2.0f : -2.0f);
        return shape == Shape.Cone ? tower + inward * 3.0f + offset : tower - inward * 3.0f - offset;
    }

    internal static WPos KoreanHelperPosition(WPos center, WPos sw, WDir south, WDir east, WPos boss, PartyRolesConfig.Assignment role, bool odd)
    {
        if (odd)
        {
            if (role is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT)
                return sw + (boss - sw).Normalized().OrthoR() * 4.5f;
            if (role is PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2)
                return sw + (sw - center).Normalized() * 4.5f;
            return center + south * 5.0f + east * 0.5f;
        }
        if (role is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT)
            return center + (-south - east).Normalized() * 6.0f;
        if (role is PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2)
            return center + (-south + east).Normalized() * 6.0f;
        return center + east * (role is PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2 ? -10.0f : 10.0f);
    }

    private void SetupPairs(int[] slots)
    {
        if (pairsLocked)
        {
            return;
        }

        pairsLocked = true;

        foreach (var pair in pairs)
        {
            if (pair.role != TowerRole.Unknown)
            {
                continue;
            }

            var shapeA = shapes[slots[(int)pair.player1Assignment]];
            var shapeB = shapes[slots[(int)pair.player2Assignment]];

            if (shapeA == Shape.None || shapeB == Shape.None)
            {
                pairsLocked = false;
                continue;
            }

            pair.role = shapeA == shapeB ? TowerRole.Helper : TowerRole.Taker;
        }

        foreach (var pair in pairs)
        {
            if (pair.role == TowerRole.Unknown)
            {
                pairsLocked = false;
                break;
            }
        }
    }
}

// Korean replacement for Kroxy. Each icon occurrence is retained, including
// consecutive occurrences of the same shape. There are four shape generations
// per player: the raidwide and that group's first three tower resolutions.
sealed class ForsakenKoreanAssignments
{
    public enum Side { None, SW, SE, Either }

    public sealed class TowerRecord
    {
        public readonly ForsakenShapes.Shape[] Shapes = new ForsakenShapes.Shape[8];
        public readonly Side[] Sides = new Side[8];
    }

    private readonly List<ForsakenShapes.Shape>[] iconHistory = Enumerable.Range(0, 8).Select(_ => new List<ForsakenShapes.Shape>(4)).ToArray();
    private readonly TowerRecord?[] history = new TowerRecord?[9];
    private readonly int[] groups = new int[8];
    private PartyRolesConfig.Assignment[]? assignments;

    public bool GroupsReady => assignments != null;
    public int Group(int slot) => groups[slot];
    public PartyRolesConfig.Assignment Assignment(int slot) => assignments![slot];
    public TowerRecord? Record(int round) => round >= 1 && round <= 8 ? history[round] : null;
    public static int ActiveGroup(int round) => round is 1 or 2 or 3 or 8 ? 1 : 2;
    private static bool IsSupport(PartyRolesConfig.Assignment role) => role is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2;

    public void ObserveIcon(int slot, ForsakenShapes.Shape shape)
    {
        if (slot >= 0 && slot < 8 && shape != ForsakenShapes.Shape.None && iconHistory[slot].Count < 4)
            iconHistory[slot].Add(shape);
    }

    public bool Initialize(int[] slots)
    {
        if (GroupsReady)
            return true;

        PartyRolesConfig.Assignment[] roles = [PartyRolesConfig.Assignment.MT, PartyRolesConfig.Assignment.OT,
            PartyRolesConfig.Assignment.H1, PartyRolesConfig.Assignment.H2, PartyRolesConfig.Assignment.M1,
            PartyRolesConfig.Assignment.M2, PartyRolesConfig.Assignment.R1, PartyRolesConfig.Assignment.R2];
        var bySlot = new PartyRolesConfig.Assignment[8];
        var seen = new bool[8];
        foreach (var role in roles)
        {
            if ((int)role >= slots.Length)
                return false;
            var slot = slots[(int)role];
            if (slot < 0 || slot >= 8 || seen[slot] || iconHistory[slot].Count == 0)
                return false;
            seen[slot] = true;
            bySlot[slot] = role;
        }

        // The initial support and DPS groups each have one stack and three
        // identical non-stack shapes; the two role groups have opposite shapes.
        var supports = Enumerable.Range(0, 8).Where(s => IsSupport(bySlot[s])).ToArray();
        var dps = Enumerable.Range(0, 8).Where(s => !IsSupport(bySlot[s])).ToArray();
        if (!ValidInitial(supports) || !ValidInitial(dps))
            return false;
        var supportShape = supports.Select(s => iconHistory[s][0]).First(s => s != ForsakenShapes.Shape.Stack);
        var dpsShape = dps.Select(s => iconHistory[s][0]).First(s => s != ForsakenShapes.Shape.Stack);
        if (supportShape == dpsShape)
            return false;

        (PartyRolesConfig.Assignment A, PartyRolesConfig.Assignment B)[] pairs = [
            (PartyRolesConfig.Assignment.MT, PartyRolesConfig.Assignment.H1),
            (PartyRolesConfig.Assignment.OT, PartyRolesConfig.Assignment.H2),
            (PartyRolesConfig.Assignment.M1, PartyRolesConfig.Assignment.R1),
            (PartyRolesConfig.Assignment.M2, PartyRolesConfig.Assignment.R2)];
        foreach (var pair in pairs)
        {
            var a = slots[(int)pair.A];
            var b = slots[(int)pair.B];
            groups[a] = groups[b] = iconHistory[a][0] == ForsakenShapes.Shape.Stack || iconHistory[b][0] == ForsakenShapes.Shape.Stack ? 1 : 2;
        }
        assignments = bySlot;
        return true;
    }

    private bool ValidInitial(int[] slots)
    {
        var first = slots.Select(s => iconHistory[s][0]).ToArray();
        return first.Count(s => s == ForsakenShapes.Shape.Stack) == 1 &&
            (first.Count(s => s == ForsakenShapes.Shape.Cone) == 3 || first.Count(s => s == ForsakenShapes.Shape.Spread) == 3);
    }

    public void Advance(int currentRound)
    {
        if (!GroupsReady)
            return;
        for (var round = 1; round <= Math.Min(currentRound, 8); ++round)
        {
            if (history[round] == null && !TryBuild(round))
                break;
        }
    }

    private bool TryBuild(int round)
    {
        var active = Enumerable.Range(0, 8).Where(s => groups[s] == ActiveGroup(round)).ToArray();
        // G1: rounds 1,2,3,8 use occurrences 0,1,2,3.
        // G2: rounds 4,5,6,7 use occurrences 0,1,2,3.
        var generation = round <= 3 ? round - 1 : round == 8 ? 3 : round - 4;
        if (active.Any(s => iconHistory[s].Count <= generation))
            return false;
        var record = new TowerRecord();
        foreach (var slot in active)
            record.Shapes[slot] = iconHistory[slot][generation];
        var odd = (round & 1) != 0;
        if (active.Count(s => record.Shapes[s] == ForsakenShapes.Shape.Stack) != (odd ? 2 : 0) ||
            active.Count(s => record.Shapes[s] == ForsakenShapes.Shape.Cone) != (odd ? 1 : 2) ||
            active.Count(s => record.Shapes[s] == ForsakenShapes.Shape.Spread) != (odd ? 1 : 2))
            return false;

        if (round == 8)
        {
            foreach (var slot in active)
                record.Sides[slot] = Side.Either;
        }
        else if (round == 4)
        {
            foreach (var slot in active)
                record.Sides[slot] = Assignment(slot) is PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2 or PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2 ? Side.SW : Side.SE;
        }
        else if (odd)
        {
            var previous = Record(round - 1);
            if (round != 1 && previous == null)
                return false;
            foreach (var slot in active)
            {
                record.Sides[slot] = record.Shapes[slot] switch
                {
                    ForsakenShapes.Shape.Cone => Side.SW,
                    ForsakenShapes.Shape.Spread => Side.SE,
                    _ => round == 1 ? (IsSupport(Assignment(slot)) ? Side.SW : Side.SE) : previous!.Sides[slot]
                };
            }
            var stacks = active.Where(s => record.Shapes[s] == ForsakenShapes.Shape.Stack).ToArray();
            if (record.Sides[stacks[0]] == record.Sides[stacks[1]])
            {
                if (previous == null)
                    return false;
                var outer = stacks.Where(s => previous.Shapes[s] == ForsakenShapes.Shape.Spread).ToArray();
                if (outer.Length != 1)
                    return false;
                record.Sides[outer[0]] = Opposite(record.Sides[outer[0]]);
            }
        }
        else // rounds 2 and 6: preserve the preceding odd round's sides
        {
            var previous = Record(round - 1);
            if (previous == null)
                return false;
            foreach (var slot in active)
                record.Sides[slot] = previous.Sides[slot];
            var sw = active.Where(s => previous.Sides[s] == Side.SW).ToArray();
            var se = active.Where(s => previous.Sides[s] == Side.SE).ToArray();
            if (sw.Length != 2 || se.Length != 2)
                return false;
            if (record.Shapes[sw[0]] == record.Shapes[sw[1]])
            {
                var outerSW = sw.Where(s => previous.Shapes[s] is ForsakenShapes.Shape.Cone or ForsakenShapes.Shape.Spread).ToArray();
                var outerSE = se.Where(s => previous.Shapes[s] is ForsakenShapes.Shape.Cone or ForsakenShapes.Shape.Spread).ToArray();
                if (outerSW.Length != 1 || outerSE.Length != 1)
                    return false;
                record.Sides[outerSW[0]] = Side.SE;
                record.Sides[outerSE[0]] = Side.SW;
            }
        }

        if (round != 8)
        {
            foreach (var side in new[] { Side.SW, Side.SE })
            {
                var occupants = active.Where(s => record.Sides[s] == side).ToArray();
                if (occupants.Length != 2)
                    return false;
                var expected = odd ? (side == Side.SW ? ForsakenShapes.Shape.Cone : ForsakenShapes.Shape.Spread) : ForsakenShapes.Shape.Cone;
                var other = odd ? ForsakenShapes.Shape.Stack : ForsakenShapes.Shape.Spread;
                if (occupants.Count(s => record.Shapes[s] == expected) != 1 || occupants.Count(s => record.Shapes[s] == other) != 1)
                    return false;
            }
        }
        // This object is never edited again after publication.
        history[round] = record;
        return true;
    }

    private static Side Opposite(Side side) => side == Side.SW ? Side.SE : Side.SW;
}


sealed class ForsakenBaitsSpreadStacks(BossModule module) : Components.UniformStackSpread(module, 5f, 5f, 3, 3)
{
    private readonly ForsakenShapes? shapes = module.FindComponent<ForsakenShapes>();
    private readonly PathOfLight? towers = module.FindComponent<PathOfLight>();

    public override void Update()
    {
        if (towers == null || shapes == null)
        {
            return;
        }

        Stacks.Clear();
        Spreads.Clear();

        foreach (var (i, player) in Raid.WithSlot(true, true, true))
        {
            if (towers.Towers.Any(t => player.Position.InCircle(t.Position, 4.00f)))
            {
                var shape = shapes.shapes[i];
                if (shape == ForsakenShapes.Shape.Stack)
                {
                    AddStack(player);
                }
                else if (shape == ForsakenShapes.Shape.Spread)
                {
                    AddSpread(player);
                }
            }
        }
    }
}

sealed class ForsakenBaitsCone(BossModule module) : Components.GenericBaitAway(module, (uint)AID.Spellwave)
{
    private readonly ForsakenShapes? shapes = module.FindComponent<ForsakenShapes>();
    private readonly PathOfLight? towers = module.FindComponent<PathOfLight>();
    private readonly AOEShapeCone cone = new(40f, 45f.Degrees());

    public override void Update()
    {
        if (towers == null || shapes == null)
        {
            return;
        }

        CurrentBaits.Clear();

        foreach (var (i, player) in Raid.WithSlot(true, true, true))
        {
            if (towers.Towers.Any(t => player.Position.InCircle(t.Position, 4.00f)))
            {
                if (shapes.shapes[i] == ForsakenShapes.Shape.Cone)
                {
                    var closestPlayer = Raid.WithoutSlot(false, true, true).Exclude(player).Closest(player.Position);
                    if (closestPlayer != null)
                    {
                        CurrentBaits.Add(new(player, closestPlayer, cone));
                    }
                }
            }
        }
    }
}

sealed class ForsakenBaitsBossClones(DMU module) : Components.UniformStackSpread(module, 5f, 5f)
{
    private readonly List<Actor> clones = []; // Also includes the boss since he will cast the same spell
    private readonly List<Actor> baiters = []; // List of players currently baiting - prevents dupes
    private readonly List<Actor> _clones = module.Enemies((uint)OID.P2KefkaHelpers);
    private readonly Actor bossP2 = module.BossP2()!;
    private int NumCasts = 0;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is ((uint)AID.FuturesEndCast) or ((uint)AID.PastsEndCast))
        {
            var count = _clones.Count;
            for (var i = 0; i < count; ++i)
            {
                clones.Add(_clones[i]);
            }
            clones.Add(bossP2);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.PastsEndSpread) or ((uint)AID.PastsEndSpread1) or ((uint)AID.FuturesEndSpread) or ((uint)AID.FuturesEndSpread1))
        {
            ++NumCasts;

            if (NumCasts == 4)
            {
                clones.Clear();
                NumCasts = 0;
            }
        }
    }

    public override void Update()
    {
        Spreads.Clear();
        baiters.Clear();

        if (clones.Count == 0)
        {
            return;
        }

        foreach (var clone in clones)
        {
            var baiter = Raid.WithoutSlot().Where(p => !baiters.Contains(p)).SortedByRange(clone.Position).Take(1).FirstOrDefault();
            if (baiter == null)
            {
                continue;
            }

            baiters.Add(baiter);
            AddSpread(baiter);
        }
    }
}

// Used for odd tower sets in figuring out what each player is responsible for
sealed class ForsakenSolverSet1(BossModule module) : BossComponent(module)
{
    private readonly ForsakenShapes? shapes = module.FindComponent<ForsakenShapes>();
    private readonly PathOfLight? towers = module.FindComponent<PathOfLight>();
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();
    private readonly DMUConfig dmuConfig = Service.Config.Get<DMUConfig>();
    public uint colourCircle = Colors.Safe;

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (shapes == null || towers == null)
        {
            return;
        }

        if (ForsakenShapes.UseKorean)
        {
            shapes.DrawKoreanPositions(pcSlot, towers, true, colourCircle);
            return;
        }

        if (towers.Towers.Count != 2 || shapes.swSoakers.None() || shapes.seSoakers.None())
        {
            return;
        }

        if ((shapes.currentTowerSet & 1) == 0)
        {
            return;
        }
        var towersSpan = CollectionsMarshal.AsSpan(towers.Towers);
        ref var towerSW = ref towersSpan[towers.CurrentSW];
        ref var towerSE = ref towersSpan[towers.CurrentSE];
        var posSW = towerSW.Position;
        var posSE = towerSE.Position;
        var midpoint = new WPos((posSW.X + posSE.X) * 0.5f, (posSW.Z + posSE.Z) * 0.5f);
        var newSouth = (midpoint - Arena.Center).Normalized();

        var towardSW = (posSW - midpoint).Normalized();
        var towardSE = (posSE - midpoint).Normalized();
        var center = Arena.Center;

        // Case: SW players with different debuffs
        if (shapes.swSoakers[pcSlot])
        {
            var shape = shapes.shapes[pcSlot];

            if (dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                if (shape == ForsakenShapes.Shape.Stack)
                {
                    Arena.ZoneCircleOutline(posSW + -towardSW * 0.5f + -newSouth * 1.0f, 1.0f, colourCircle, 2.0f);
                }
                else if (shape == ForsakenShapes.Shape.Cone)
                {
                    Arena.ZoneCircleOutline(posSW + newSouth * 3.0f, 1.0f, colourCircle, 2.0f);
                }
            }

        }

        // Case: SW players with same debuffs
        else if (shapes.supportHelpers[pcSlot])
        {
            var assignment = partyConfig[Raid.Members[pcSlot].ContentId];

            if (dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                if (assignment is PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2)
                {
                    Arena.ZoneCircleOutline(posSW + newSouth * 4.5f, 1.0f, colourCircle, 2.0f);
                }
                else if (assignment is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT)
                {
                    Arena.ZoneCircleOutline(posSW - towardSW * 3.0f - newSouth * 4.0f, 1.0f, colourCircle, 2.0f);
                }
            }

        }

        // Case: SE players with different debuffs
        else if (shapes.seSoakers[pcSlot])
        {
            var shape = shapes.shapes[pcSlot];

            if (dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                if (shape == ForsakenShapes.Shape.Stack)
                {
                    Arena.ZoneCircleOutline(posSE - towardSE * 2.5f + newSouth * 2.5f, 1.0f, colourCircle, 2.0f);
                }
                else if (shape == ForsakenShapes.Shape.Spread)
                {
                    Arena.ZoneCircleOutline(posSE + towardSE * 2.0f - newSouth * 3.0f, 1.0f, colourCircle, 2.0f);
                }
            }

        }

        // Case: SE players with same debuffs
        else if (shapes.dpsHelpers[pcSlot])
        {
            var assignment = partyConfig[Raid.Members[pcSlot].ContentId];

            if (dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                if (assignment is PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2 or PartyRolesConfig.Assignment.R1 or PartyRolesConfig.Assignment.R2)
                {
                    Arena.ZoneCircleOutline(posSE - towardSE * 4.0f + newSouth * 3.0f, 1.0f, colourCircle, 2.0f);
                }
            }

        }
    }

    public override void Update()
    {
        if (shapes == null || towers == null)
        {
            return;
        }

        if (ForsakenShapes.UseKorean)
        {
            shapes.UpdateKoreanTowerRestrictions(towers, true);
            return;
        }

        if (towers.Towers.Count != 2)
        {
            return;
        }

        if ((shapes.currentTowerSet & 1) == 0)
        {
            return;
        }

        var party = new BitMask(0xFF);
        var towersSpan = CollectionsMarshal.AsSpan(towers.Towers);
        var tSE = towers.CurrentSE;
        var tSW = towers.CurrentSW;
        for (var i = 0; i < 2; ++i)
        {
            ref var t = ref towersSpan[i];
            if (i == tSW)
            {
                t.ForbiddenSoakers = party & ~shapes.swSoakers;
            }
            else if (i == tSE)
            {
                t.ForbiddenSoakers = party & ~shapes.seSoakers;
            }
        }
    }
}

// Used for even tower sets in figuring out what each player is responsible for
sealed class ForsakenSolverSet2(BossModule module) : BossComponent(module)
{
    private readonly ForsakenShapes? shapes = module.FindComponent<ForsakenShapes>();
    private readonly PathOfLight? towers = module.FindComponent<PathOfLight>();
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();
    private readonly DMUConfig dmuConfig = Service.Config.Get<DMUConfig>();

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (shapes == null || towers == null)
        {
            return;
        }

        if (ForsakenShapes.UseKorean)
        {
            shapes.DrawKoreanPositions(pcSlot, towers, false, Colors.Safe);
            return;
        }

        if (towers.Towers.Count != 2 || shapes.swSoakers.None() || shapes.seSoakers.None())
        {
            return;
        }

        if ((shapes.currentTowerSet & 1) != 0)
        {
            return;
        }
        var towersSpan = CollectionsMarshal.AsSpan(towers.Towers);
        var towerSW = towersSpan[towers.CurrentSW].Position;
        var towerSE = towersSpan[towers.CurrentSE].Position;
        var center = Arena.Center;
        // Case: SW players with different debuffs (soakers)
        if (shapes.swSoakers[pcSlot])
        {
            var toCenter = (center - towerSW).Normalized();
            if (dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                if (shapes.shapes[pcSlot] == ForsakenShapes.Shape.Cone)
                {
                    Arena.ZoneCircleOutline(towerSW + toCenter * 3.5f, 0.75f, Colors.Safe, 1.0f);
                }
                else if (shapes.shapes[pcSlot] == ForsakenShapes.Shape.Spread)
                {
                    if (dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Meow_Markerless)
                    {
                        Arena.ZoneCircleOutline(towerSW - toCenter * 3.5f, 0.75f, Colors.Safe, 1.0f);
                    }
                    else if (dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
                    {
                        Arena.ZoneCircleOutline(towerSW + (-toCenter).Rotate(34f.Degrees()) * 3.57f, 0.75f, Colors.Safe, 1.0f);
                    }
                }
            }

        }

        // Case: SW players with same debuffs (helpers)
        else if (shapes.supportHelpers[pcSlot])
        {
            var assignment = partyConfig[Raid.Members[pcSlot].ContentId];

            var toCenter = (center - towerSW).Normalized();

            if (dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                if (assignment is PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2)
                {
                    if (dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Meow_Markerless)
                    {
                        Arena.ZoneCircleOutline(towerSW + toCenter.OrthoL() * 4.5f, 0.75f, Colors.Safe, 1.0f);
                    }
                    else if (dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
                    {
                        Arena.ZoneCircleOutline(towerSW + toCenter.Rotate(82f.Degrees()) * 7.07f, 0.75f, Colors.Safe, 1.0f);
                    }
                }
                else if (assignment is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT)
                {
                    Arena.ZoneCircleOutline(towerSW + toCenter.Rotate(35.0f.Degrees()) * 11.5f, 0.75f, Colors.Safe, 1.0f);
                }
            }

        }

        // Case: SE players with different debuffs (soakers)
        else if (shapes.seSoakers[pcSlot])
        {
            var toCenter = (Arena.Center - towerSE).Normalized();

            if (dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                if (shapes.shapes[pcSlot] == ForsakenShapes.Shape.Cone)
                {
                    Arena.ZoneCircleOutline(towerSE + toCenter * 3.5f, 0.75f, Colors.Safe, 1.0f);
                }
                else if (shapes.shapes[pcSlot] == ForsakenShapes.Shape.Spread)
                {
                    if (dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Meow_Markerless)
                    {
                        Arena.ZoneCircleOutline(towerSE - toCenter * 3.5f, 0.75f, Colors.Safe, 1.0f);
                    }
                    else if (dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
                    {
                        Arena.ZoneCircleOutline(towerSE + (-toCenter).Rotate(-26f.Degrees()) * 3.6f, 0.75f, Colors.Safe, 1.0f);
                    }
                }
            }

        }

        // Case: SE players with same debuffs (helpers)
        else if (shapes.dpsHelpers[pcSlot])
        {
            var assignment = partyConfig[Raid.Members[pcSlot].ContentId];
            var toCenter = (center - towerSE).Normalized();

            if (dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                if (assignment is PartyRolesConfig.Assignment.R1 or PartyRolesConfig.Assignment.R2)
                {
                    if (dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Meow_Markerless)
                    {
                        Arena.ZoneCircleOutline(towerSE + toCenter.OrthoR() * 4.5f, 0.75f, Colors.Safe, 1.0f);
                    }
                    else if (dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
                    {
                        Arena.ZoneCircleOutline(towerSE + toCenter.Rotate(-82f.Degrees()) * 7.07f, 0.75f, Colors.Safe, 1.0f);
                    }
                }
                else if (assignment is PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2)
                {
                    Arena.ZoneCircleOutline(towerSE + toCenter.Rotate(-35.0f.Degrees()) * 11.5f, 0.75f, Colors.Safe, 1.0f);
                }
            }

        }
    }

    public override void Update()
    {
        if (shapes == null || towers == null)
        {
            return;
        }

        if (ForsakenShapes.UseKorean)
        {
            shapes.UpdateKoreanTowerRestrictions(towers, false);
            return;
        }

        if (towers.Towers.Count != 2)
        {
            return;
        }

        if ((shapes.currentTowerSet & 1) != 0)
        {
            return;
        }

        var party = new BitMask(0xFF);
        var towersSpan = CollectionsMarshal.AsSpan(towers.Towers);
        var tSE = towers.CurrentSE;
        var tSW = towers.CurrentSW;
        for (var i = 0; i < 2; ++i)
        {
            ref var t = ref towersSpan[i];
            if (i == tSW)
            {
                t.ForbiddenSoakers = party & ~shapes.swSoakers;
            }
            else if (i == tSE)
            {
                t.ForbiddenSoakers = party & ~shapes.seSoakers;
            }
        }
    }
}

sealed class WingsOfDestructionLeftRight(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.WingsOfDestructionLeft, (uint)AID.WingsOfDestructionRight], new AOEShapeRect(80f, 20f));

sealed class WingsOfDestructionTB(BossModule module) : Components.GenericBaitAway(module, (uint)AID.WingsOfDestructionTB, true, true)
{
    private Actor? casterPosition;
    private readonly AOEShapeCircle circle = new(7f);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.WingsOfDestructionTB)
        {
            casterPosition = caster;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.WingsOfDestructionTB1)
        {
            casterPosition = null;
            NumCasts++;
        }
    }

    public override void Update()
    {
        CurrentBaits.Clear();

        if (casterPosition == null)
        {
            return;
        }

        var players = Raid.WithoutSlot().SortedByRange(casterPosition.Position).ToList();
        if (players.Count > 1)
        {
            CurrentBaits.Add(new(casterPosition, players[0], circle));
            CurrentBaits.Add(new(casterPosition, players[^1], circle));
        }
    }
}

sealed class Trine(DMU module) : Components.GenericAOEs(module, (uint)AID.Trine)
{
    private readonly List<AOEInstance> aoes = [];
    private readonly List<int> firstWaveSectors = new(3);
    private (float MT, float OT, float Party)? directions;
    private const float radius = 5.77350269189626f; // 10f * MathF.Sqrt(3f) / 3f;
    private const float halfradius = 5.77350269189626f * 0.5f;
    private readonly AOEShapeCircle circle = new(6f);
    private readonly PartyRolesConfig partyConfig = Service.Config.Get<PartyRolesConfig>();

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID is var oid && oid is not (uint)OID.YellowTriangle and not (uint)OID.YellowTriangle1)
        {
            return;
        }

        RecordFirstWave(actor.Position);

        var direction = oid == (uint)OID.YellowTriangle ? 1f : -1f;
        var pos = actor.Position;

        aoes.Add(new(circle, pos + new WDir(direction * radius, 0f)));
        aoes.Add(new(circle, pos + new WDir(direction * -halfradius, 5f)));
        aoes.Add(new(circle, pos + new WDir(direction * -halfradius, -5f)));
    }

    public override void OnActorEAnim(Actor actor, uint state)
    {
        if (actor.OID is ((uint)OID.YellowTriangle) or ((uint)OID.YellowTriangle1))
        {
            if (state == (uint)Animations.TriangleExplosion)
            {
                aoes.RemoveRange(0, 3);
                NumCasts += 3;
            }
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        if (aoes.Count == 0)
        {
            return CollectionsMarshal.AsSpan(aoes);
        }

        (int currentWave, int nextWave)[] wave = [(9, 3), (3, 9), (9, 0)];
        var (currentSize, nextSize) = wave[NumCasts < 9 ? 0 : NumCasts < 12 ? 1 : 2];
        var count = Math.Min(currentSize + nextSize, aoes.Count);
        for (var i = 0; i < count; ++i)
        {
            aoes[i] = aoes[i] with
            {
                Color = i < currentSize ? Colors.Danger : default,
                Risky = i < currentSize
            };
        }

        return CollectionsMarshal.AsSpan(aoes)[..count];
    }

    // Fixed north is 0 degrees; sectors increase clockwise in 60-degree steps.
    // Only the first three distinct outer positions determine the strategy.
    private void RecordFirstWave(WPos position)
    {
        if (directions != null)
            return;

        var offset = position - Arena.Center;
        if (offset.LengthSq() <= 1f) // The central triangle is not a pattern slot.
            return;

        var degrees = MathF.Atan2(offset.X, -offset.Z) * 180f / MathF.PI;
        var sector = (int)MathF.Round((degrees + 360f) / 60f) % 6;
        if (firstWaveSectors.Contains(sector))
            return;
        firstWaveSectors.Add(sector);
        if (firstWaveSectors.Count != 3)
            return;

        // Three adjacent triangles: the middle triangle defines relative north.
        foreach (var start in firstWaveSectors)
        {
            if (firstWaveSectors.Contains((start + 1) % 6) &&
                firstWaveSectors.Contains((start + 2) % 6))
            {
                var north = ((start + 1) % 6) * 60f;
                directions = (north - 60f, north, north + 60f);
                return;
            }
        }

        // One adjacent pair: its angular midpoint defines relative north.
        foreach (var start in firstWaveSectors)
        {
            var next = (start + 1) % 6;
            if (!firstWaveSectors.Contains(next))
                continue;

            var isolated = firstWaveSectors.Single(s => s != start && s != next);
            var north = start * 60f + 30f;
            directions = (north - 30f, north + 30f, isolated * 60f);
            return;
        }

        // Alternating triangles use the fixed north of the arena.
        directions = firstWaveSectors.Contains(0) ? (240f, 0f, 120f) : (300f, 180f, 60f);
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        // Keep the existing timing: show destinations after the first explosions.
        if (NumCasts < 9 || directions is not { } positions || pcSlot < 0 || pcSlot >= 8)
            return;

        var slots = partyConfig.SlotsPerAssignment(Raid);
        if (slots.Length == 0)
            return;
        var assignment = partyConfig[Raid.Members[pcSlot].ContentId];
        var (degrees, distance) = assignment switch
        {
            PartyRolesConfig.Assignment.MT => (positions.MT, 20f),
            PartyRolesConfig.Assignment.OT => (positions.OT, 16f),
            _ => (positions.Party, 18f)
        };

        var radians = degrees * MathF.PI / 180f;
        var direction = new WDir(MathF.Sin(radians), -MathF.Cos(radians));
        Arena.ZoneCircleOutline(Arena.Center + direction * distance, 1f, Colors.Safe, 2f);
    }
}
