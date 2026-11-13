using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pixel8.Core.Cards;
using Pixel8.Core.Combat;
using Pixel8.Core.Content;
using Pixel8.Core.Heroes;
using Pixel8.Desktop.Fx;
using Pixel8.Desktop.Rendering;
using Pixel8.Desktop.Ui;

namespace Pixel8.Desktop.Screens;

/// <summary>
/// M3 combat screen. All rules live in <see cref="CombatState"/>; this screen only
/// (1) replays <see cref="CombatState.Events"/> at a readable pace (HP bars, floating numbers, shake, log) and
/// (2) turns clicks and keys into PlayCard / UseBasic / EndTurn / Flee / ResolveReaction calls.
/// Input is ignored while events are still playing.
///
/// Layout (256×144): heroes left, enemies right (facing the party), log/card info in the middle,
/// the active hero's hand along the bottom with Strike / Guard / End turn / Flee on the right.
/// Keys: 1–5 card · S strike · G guard · E end turn · F flee · 1–4 pick target · Esc/right-click cancel · Y/N reaction
/// and Luck reroll · Space/Enter/click to time a big attack.
///
/// Session 5: every d20 and damage roll plays out on the <see cref="RollStrip"/> at the bottom of the middle panel;
/// a failed hero attack offers a Luck reroll there; big enemy attacks open the timing bar.
/// </summary>
public sealed class CombatScreen : IScreen
{
    private const int TopY = 14;
    private const int HeroRow = 24;
    private const int EnemyRow = 20;
    private const int HeroX = 6;
    private const int EnemyX = 242;
    private const int InfoX = 74, InfoY = 12, InfoW = 104, InfoH = 77, InfoChars = 25;
    private const int HandY = 100, CardW = 30, CardH = 40, CardGap = 2, MaxCards = 5;
    private const int ButtonsX = 172, ButtonW = 80, ButtonH = 9, HalfW = 39;

    // Two half-width rows (Strike/Guard, class basic/Bag) then End turn and Flee at full width.
    private static readonly Rectangle StrikeButton = new(ButtonsX, HandY, HalfW, ButtonH);
    private static readonly Rectangle GuardButton = new(ButtonsX + ButtonW - HalfW, HandY, HalfW, ButtonH);
    private static readonly Rectangle BasicButton = new(ButtonsX, HandY + 10, HalfW, ButtonH);
    private static readonly Rectangle ItemButton = new(ButtonsX + ButtonW - HalfW, HandY + 10, HalfW, ButtonH);
    private static readonly Rectangle EndButton = new(ButtonsX, HandY + 20, ButtonW, ButtonH);
    private static readonly Rectangle FleeButton = new(ButtonsX, HandY + 30, ButtonW, ButtonH);

    private readonly CombatState _c;
    private readonly Action<CombatPhase> _onDone;
    private readonly ScreenShake _shake = new();
    private readonly Dictionary<Combatant, int> _shownHp = new();
    private readonly Dictionary<Combatant, float> _flash = new();
    private readonly List<Floater> _floaters = new();
    private readonly List<(string Text, Color Color)> _log = new();
    private readonly RollStrip _strip = new(new Rectangle(InfoX + 1, InfoY + InfoH - RollStrip.Height - 1, InfoW - 2, RollStrip.Height));
    private readonly Random _fx = new();

    // Timed reaction (big attacks). The rules never see the clock: the grade is measured here.
    private const float TimingReady = 0.7f, TimingSweep = 1.1f, GradeHold = 0.7f;
    private PendingTiming? _timingFor;
    private float _timingT;
    private double _timingCenter;
    private TimingGrade? _timingGrade;
    private float _gradeShow;

    private int _cursor;
    private float _wait;
    private float _time;
    private bool _finished;

    private CardInstance? _selected;
    private CardDef? _basicSelected;
    private string? _itemSelected;

    // Little animations: attack lunges, spell sparkles, shield flashes (seconds left).
    private const float LungeTime = 0.3f, CastTime = 0.5f, ShieldTime = 0.45f;
    private readonly Dictionary<Combatant, float> _lunge = new();
    private readonly Dictionary<Combatant, float> _cast = new();
    private readonly Dictionary<Combatant, float> _shieldFx = new();

    // Hit and spell effects (session 7). The card being resolved picks its "fx"; a crit adds a star burst.
    private readonly VfxPlayer _vfx = new();
    private CardDef? _fxCard;
    private Combatant? _critTarget;
    private CardInstance? _hoverCard;
    private Combatant? _hoverTarget;
    private string _hint = "";
    private Point _pointer;

    private sealed class Floater
    {
        public string Text = "";
        public Vector2 Pos;
        public Color Color;
        public float Ttl;
    }

    public CombatScreen(CombatState combat, Action<CombatPhase> onDone)
    {
        _c = combat;
        _onDone = onDone;
        foreach (var h in combat.Heroes) _shownHp[h] = h.Hp;
        foreach (var e in combat.Enemies) _shownHp[e] = e.Hp;

        string kind = combat.Kind switch
        {
            CombatKind.Boss => "THE BOSS BLOCKS THE STAIRS!",
            CombatKind.Elite => "AN ELITE FOE!",
            _ => "AMBUSH!"
        };
        AddLog(kind, Palette.Pink);
        if (combat.Phase == CombatPhase.NotStarted) combat.Start();
    }

    public Point ShakeOffset => _shake.Offset;

    private bool Busy => _cursor < _c.Events.Count || _wait > 0;
    private bool Targeting => _selected is not null || _basicSelected is not null || _itemSelected is not null;

    private TargetSide CurrentSide() =>
        _selected is not null ? _c.TargetSideFor(_selected.Def)
        : _basicSelected is not null ? _c.TargetSideFor(_basicSelected)
        : _itemSelected is not null ? _c.TargetSideFor(_c.Content.Items[_itemSelected])
        : TargetSide.None;

    /// <summary>The card (or basic) being aimed, for previews; null for items.</summary>
    private CardDef? AimedCard => _selected?.Def ?? _basicSelected;

    private string AimedName =>
        _selected?.DisplayName ?? _basicSelected?.Name ?? (_itemSelected is null ? "" : _c.Content.Items[_itemSelected].Name);

    // ================================================================== update

    public void Update(float dt, Input input)
    {
        _time += dt;
        _pointer = input.Pointer;
        _shake.Update(dt);
        foreach (var timers in new[] { _flash, _lunge, _cast, _shieldFx })
            foreach (var k in timers.Keys.ToList())
            {
                timers[k] -= dt;
                if (timers[k] <= 0) timers.Remove(k);
            }
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            var f = _floaters[i];
            f.Ttl -= dt;
            f.Pos.Y -= dt * 12;
            if (f.Ttl <= 0) _floaters.RemoveAt(i);
        }

        // Playback: hold Space or the mouse button to speed it up (the roll strip too).
        float speed = input.Held(Keys.Space) || input.LeftHeld ? 3f : 1f;
        _strip.Update(dt * speed);
        _vfx.Update(dt * speed);
        _wait -= dt * speed;
        while (_wait <= 0 && _cursor < _c.Events.Count)
            _wait = Play(_c.Events[_cursor++]);
        if (Busy) return;

        switch (_c.Phase)
        {
            case CombatPhase.HeroTurn:
                UpdateHeroTurn(input);
                break;
            case CombatPhase.AwaitingReaction:
                UpdateReaction(input);
                break;
            case CombatPhase.AwaitingLuck:
                UpdateLuck(input);
                break;
            case CombatPhase.AwaitingTiming:
                UpdateTiming(dt, input);
                break;
            default:
                if (_c.IsOver && !_finished && (input.LeftClick || input.PressedAny(Keys.Space, Keys.Enter)))
                {
                    _finished = true;
                    SyncHp();
                    _onDone(_c.Phase);
                }
                break;
        }
    }

    private void UpdateHeroTurn(Input input)
    {
        var hero = _c.ActiveHero!;
        var hand = hero.Deck.Hand;

        _hoverCard = null;
        _hoverTarget = null;
        for (int i = 0; i < Math.Min(hand.Count, MaxCards); i++)
            if (input.Over(BaseCardRect(i))) _hoverCard = hand[i];
        foreach (var t in AllCombatants())
            if (input.Over(HitBox(t))) _hoverTarget = t;

        if (Targeting)
        {
            if (input.RightClick || input.Pressed(Keys.Escape))
            {
                CancelTargeting();
                return;
            }

            var valid = ValidTargets(CurrentSide());
            int digit = input.PressedDigit();
            Combatant? chosen = null;
            if (digit >= 0 && digit < valid.Count) chosen = valid[digit];
            else if (input.LeftClick && _hoverTarget is not null && valid.Contains(_hoverTarget)) chosen = _hoverTarget;
            else if (input.LeftClick && _hoverCard is not null) { Select(_hoverCard); return; }

            if (chosen is not null)
            {
                if (_basicSelected is not null) _c.UseBasic(_basicSelected.Id, chosen);
                else if (_itemSelected is not null) _c.UseItem(_itemSelected, chosen);
                else _c.PlayCard(_selected!, chosen);
                CancelTargeting();
            }
            return;
        }

        int d = input.PressedDigit();
        if (d >= 0 && d < Math.Min(hand.Count, MaxCards)) { Select(hand[d]); return; }
        if (input.LeftClick && _hoverCard is not null) { Select(_hoverCard); return; }

        if (input.Clicked(StrikeButton) || input.Pressed(Keys.S))
        {
            UseBasicAction(_c.Content.Card(ContentDb.BasicStrikeId));
            return;
        }
        if (input.Clicked(GuardButton) || input.Pressed(Keys.G))
        {
            UseBasicAction(_c.Content.Card(ContentDb.BasicGuardId));
            return;
        }
        if (input.Clicked(BasicButton) || input.Pressed(Keys.C))
        {
            var basic = _c.ClassBasic(hero);
            if (basic is null) _hint = "NO CLASS ACTION";
            else UseBasicAction(basic);
            return;
        }
        if (input.Clicked(ItemButton) || input.Pressed(Keys.B))
        {
            UseBagItem(hero);
            return;
        }
        if (input.Clicked(EndButton) || input.PressedAny(Keys.E, Keys.Enter))
        {
            _hint = "";
            _c.EndTurn();
            return;
        }
        if (input.Clicked(FleeButton) || input.Pressed(Keys.F))
        {
            if (_c.CanFlee(out string reason)) _c.Flee();
            else _hint = reason.ToUpperInvariant();
        }
    }

    private void Select(CardInstance card)
    {
        if (!_c.CanPlay(card))
        {
            _hint = card.Def.Speed switch
            {
                CardSpeed.Reaction => "REACTIONS ARE PLAYED WHEN YOU'RE HIT",
                CardSpeed.Quick => "QUICK ALREADY USED THIS TURN",
                _ => "ACTION ALREADY USED THIS TURN"
            };
            return;
        }

        _hint = "";
        var side = _c.TargetSideFor(card.Def);
        var valid = ValidTargets(side);
        if (side == TargetSide.None)
        {
            CancelTargeting();
            _c.PlayCard(card);
        }
        else if (side == TargetSide.Enemy && valid.Count == 1)
        {
            CancelTargeting();
            _c.PlayCard(card, valid[0]);
        }
        else
        {
            CancelTargeting();
            _selected = card;
        }
    }

    /// <summary>Strike, Guard or the class basic: use at once when there's nothing (or only one thing) to aim at.</summary>
    private void UseBasicAction(CardDef def)
    {
        if (!_c.CanUseBasic(def))
        {
            _hint = def.Exhaust ? $"{def.Name} IS ONCE PER FIGHT".ToUpperInvariant() : $"{def.Speed} ALREADY USED".ToUpperInvariant();
            Sfx.Play("deny");
            return;
        }
        _hint = "";
        var side = _c.TargetSideFor(def);
        var valid = ValidTargets(side);
        CancelTargeting();
        if (side == TargetSide.None) _c.UseBasic(def.Id);
        else if (valid.Count == 1) _c.UseBasic(def.Id, valid[0]);
        else _basicSelected = def;
    }

    /// <summary>B: the first item in the Bag, as a Quick action (healing potions aim at an ally).</summary>
    private void UseBagItem(Hero hero)
    {
        string? item = _c.Party.Bag.FirstOrDefault(id => _c.Content.Items.ContainsKey(id));
        if (item is null) { _hint = "THE BAG IS EMPTY"; Sfx.Play("deny"); return; }
        if (!_c.CanUseItem(item)) { _hint = "QUICK ALREADY USED"; Sfx.Play("deny"); return; }
        _hint = "";
        var side = _c.TargetSideFor(_c.Content.Items[item]);
        var valid = ValidTargets(side);
        CancelTargeting();
        if (side == TargetSide.None) _c.UseItem(item);
        else if (valid.Count == 1) _c.UseItem(item, valid[0]);
        else _itemSelected = item;
    }

    private void CancelTargeting()
    {
        _selected = null;
        _basicSelected = null;
        _itemSelected = null;
    }

    private void UpdateReaction(Input input)
    {
        var p = _c.Pending!;
        for (int i = 0; i < p.Options.Count; i++)
        {
            if (input.Clicked(ReactionRect(i)) || (i == 0 && input.Pressed(Keys.Y)))
            {
                _c.ResolveReaction(p.Options[i]);
                return;
            }
        }
        if (input.Clicked(ReactionRect(p.Options.Count)) || input.Pressed(Keys.N))
            _c.ResolveReaction(null);
    }

    private Rectangle LuckYesRect => new(_strip.Area.X + 3, _strip.Area.Y + 20, 58, 9);
    private Rectangle LuckNoRect => new(_strip.Area.X + 64, _strip.Area.Y + 20, _strip.Area.Width - 67, 9);

    private void UpdateLuck(Input input)
    {
        if (input.Clicked(LuckYesRect) || input.Pressed(Keys.Y))
        {
            Sfx.Play("confirm");
            _c.ResolveLuck(true);
        }
        else if (input.Clicked(LuckNoRect) || input.Pressed(Keys.N))
        {
            Sfx.Play("select");
            _c.ResolveLuck(false);
        }
    }

    private double TimingMarker => Math.Clamp((_timingT - TimingReady) / TimingSweep, 0f, 1f);

    private void UpdateTiming(float dt, Input input)
    {
        var p = _c.PendingTiming!;
        if (!ReferenceEquals(_timingFor, p))
        {
            _timingFor = p;
            _timingT = 0;
            _timingGrade = null;
            _gradeShow = 0;
            _timingCenter = 0.35 + _fx.NextDouble() * 0.5;
            Sfx.Play("warn");
        }

        if (_timingGrade is TimingGrade done)
        {
            _gradeShow += dt;
            if (_gradeShow >= GradeHold)
            {
                _timingFor = null;
                _timingGrade = null;
                _c.ResolveTiming(done);
            }
            return;
        }

        _timingT += dt;
        bool press = input.PressedAny(Keys.Space, Keys.Enter, Keys.Z) || input.LeftClick;
        if (_timingT >= TimingReady && press) SetGrade(TimingRules.Grade(TimingMarker, _timingCenter, p.Window));
        else if (_timingT >= TimingReady + TimingSweep) SetGrade(TimingGrade.Miss);
    }

    private void SetGrade(TimingGrade grade)
    {
        _timingGrade = grade;
        Sfx.Play(grade switch
        {
            TimingGrade.Perfect => "perfect",
            TimingGrade.Good => "good",
            TimingGrade.Ok => "select",
            _ => "deny"
        });
        if (grade == TimingGrade.Perfect) _shake.Trigger(1, 0.1f);
    }

    // ================================================================== playback

    /// <summary>Applies one event to the display and returns how long to linger on it.</summary>
    private float Play(GameEvent e)
    {
        string? line = CombatText.Line(e);
        if (line is not null) AddLog(line, LogColor(e));

        switch (e)
        {
            case RoundStarted:
                return 0.35f;
            case TurnStarted t:
                _fxCard = null;
                return t.Actor is Enemy ? 0.25f : 0.05f;
            case CardPlayed cp:
                _fxCard = cp.Actor is Hero && cp.Card.Speed != CardSpeed.Reaction ? cp.Card : _fxCard;
                if (cp.Card.Speed == CardSpeed.Reaction) _vfx.Spawn("shield", Center(cp.Actor), Center(cp.Actor));
                if (cp.Card.Speed == CardSpeed.Reaction)
                {
                    _shieldFx[cp.Actor] = ShieldTime;
                    Sfx.Play("block");
                }
                else if (IsSpell(cp.Card))
                {
                    _cast[cp.Actor] = CastTime;
                    Sfx.Play("part", 0.8f, 0.5f);
                }
                return 0.25f;
            case CheckRolled cr:
                if (cr.Versus == "AC") _lunge[cr.Roller] = LungeTime;
                return _strip.Show(cr);
            case ItemUsed iu:
                Float(iu.Hero, Gui.Clip(iu.Item.Name.ToUpperInvariant(), 10), Palette.Green, -6);
                Sfx.Play("select");
                return 0.3f;
            case DamageRolled dr:
            {
                _critTarget = dr.Critical ? dr.Target : null;
                // Projectiles launch while the dice roll, so they land as the damage does.
                string kind = FxFor(dr.Source, dr.Type);
                if (VfxPlayer.Lead(kind) > 0) _vfx.Spawn(kind, Center(dr.Source), Center(dr.Target));
                return _strip.Show(dr);
            }
            case LuckSpent ls:
                Float(ls.Hero, "LUCK!", Palette.Green, -6);
                Sfx.Play("coin");
                return 0.3f;
            case TimingResolved tr:
                Float(tr.Defender, TimingRules.Label(tr.Grade) + "!", GradeColor(tr.Grade), -6);
                return 0.3f;
            case DamageDealt d:
            {
                string kind = FxFor(d.Source, d.Type);
                if (VfxPlayer.Lead(kind) == 0) _vfx.Spawn(kind, Center(d.Source), Center(d.Target));
                if (ReferenceEquals(_critTarget, d.Target))
                {
                    _vfx.Spawn("crit", Center(d.Target), Center(d.Target));
                    _critTarget = null;
                }
            }
                _shownHp[d.Target] = Math.Max(0, Shown(d.Target) - d.Amount);
                _flash[d.Target] = 0.3f;
                Float(d.Target, d.Amount > 0 ? $"-{d.Amount}" : "BLOCKED", Palette.Red, 0);
                if (d.Target is Hero) _shake.Trigger(2, 0.2f);
                else _shake.Trigger(1, 0.1f);
                Sfx.Play(d.Amount == 0 ? "block" : d.Target is Hero ? "hurt" : "hit");
                return 0.35f;
            case Healed h:
                _shownHp[h.Target] = Math.Min(h.Target.MaxHp, Shown(h.Target) + h.Amount);
                Float(h.Target, $"+{h.Amount}", Palette.Green, 0);
                _vfx.Spawn("heal", Center(h.Target), Center(h.Target));
                Sfx.Play("heal");
                return 0.3f;
            case BlockGained bg:
                Float(bg.Target, $"+{bg.Amount}", Palette.Blue, 0);
                _vfx.Spawn("shield", Center(bg.Target), Center(bg.Target));
                Sfx.Play("block");
                return 0.25f;
            case ConditionApplied ca:
                Float(ca.Target, ca.Condition.ToUpperInvariant(), Palette.Pink, 0);
                return 0.2f;
            case HeroDowned:
                _shake.Trigger(3, 0.3f);
                Sfx.Play("down");
                return 0.5f;
            case CombatantDied died:
                _vfx.Spawn("poof", Center(died.Combatant), Center(died.Combatant));
                Sfx.Play("die");
                return 0.4f;
            case DeathSaveRolled:
                return 0.5f;
            case RollModified:
                return 0.4f;
            case CombatEnded ce:
                SyncHp();
                Sfx.Play(ce.Outcome == CombatPhase.Victory ? "coin" : ce.Outcome == CombatPhase.Defeat ? "down" : "select");
                return 0.3f;
            default:
                return 0f;
        }
    }

    private Point Center(Combatant c) => PosOf(c) + new Point(4, 4);

    /// <summary>
    /// Which effect a hit gets: an enemy's intent "fx" (looked up by the damage type, which is the intent name);
    /// for heroes the card's "fx", else the damage type (fire, force, radiant), else the hero's weaponFx.
    /// </summary>
    private string FxFor(Combatant source, string type)
    {
        if (source is Enemy enemy)
            return enemy.Def.Intents.FirstOrDefault(i => i.Name == type)?.Fx ?? "slash";
        if (_fxCard?.Fx is { Length: > 0 } cardFx) return cardFx;
        return type switch
        {
            "fire" => "fire",
            "force" => "missile",
            "radiant" => "radiant",
            _ => (source as Hero)?.Class.WeaponFx ?? "slash"
        };
    }

    private static Color GradeColor(TimingGrade g) => g switch
    {
        TimingGrade.Perfect => Palette.Yellow,
        TimingGrade.Good => Palette.Green,
        TimingGrade.Ok => Palette.Blue,
        _ => Palette.Red
    };

    private int Shown(Combatant c) => _shownHp.TryGetValue(c, out int v) ? v : c.Hp;

    private void SyncHp()
    {
        foreach (var c in _shownHp.Keys.ToList()) _shownHp[c] = c.Hp;
    }

    private void Float(Combatant c, string text, Color color, int dy)
    {
        var p = PosOf(c);
        int x = c is Hero ? p.X + 2 : p.X - TinyFont.Measure(text) + 6;
        _floaters.Add(new Floater { Text = text, Pos = new Vector2(x, p.Y - 4 + dy), Color = color, Ttl = 0.8f });
    }

    private void AddLog(string text, Color color)
    {
        foreach (string raw in text.Split('\n'))
            foreach (string line in Gui.Wrap(raw, InfoChars))
                _log.Add((line, color));
        if (_log.Count > 60) _log.RemoveRange(0, _log.Count - 60);
    }

    private static Color LogColor(GameEvent e) => e switch
    {
        RoundStarted => Palette.Yellow,
        DamageDealt => Palette.Orange,
        Healed => Palette.Green,
        BlockGained => Palette.Blue,
        HeroDowned or CombatantDied => Palette.Pink,
        CheckRolled => Palette.LightGrey,
        _ => Palette.White
    };

    // ================================================================== layout

    private IEnumerable<Combatant> AllCombatants() => _c.Heroes.Cast<Combatant>().Concat(_c.Enemies);

    private Point PosOf(Combatant c)
    {
        if (c is Hero h) return new Point(HeroX, TopY + IndexOf(_c.Heroes, h) * HeroRow);
        return new Point(EnemyX, TopY + IndexOf(_c.Enemies, (Enemy)c) * EnemyRow);
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, T item) where T : class
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return 0;
    }

    private Rectangle HitBox(Combatant c)
    {
        var p = PosOf(c);
        return c is Hero
            ? new Rectangle(p.X - 2, p.Y - 2, 66, HeroRow - 2)
            : new Rectangle(InfoX + InfoW + 2, p.Y - 2, 256 - (InfoX + InfoW + 2), EnemyRow);
    }

    private List<Combatant> ValidTargets(TargetSide side) => side switch
    {
        TargetSide.Enemy => _c.Enemies.Where(e => e.IsAlive).Cast<Combatant>().ToList(),
        TargetSide.Ally => _c.Heroes.Where(h => h.IsAlive).Cast<Combatant>().ToList(),
        _ => new List<Combatant>()
    };

    private static Rectangle BaseCardRect(int i) => new(4 + i * (CardW + CardGap), HandY, CardW, CardH);

    private Rectangle CardRect(int i, CardInstance card)
    {
        int lift = ReferenceEquals(card, _selected) ? 4 : ReferenceEquals(card, _hoverCard) ? 2 : 0;
        var r = BaseCardRect(i);
        r.Y -= lift;
        return r;
    }

    private Rectangle ReactionBox => new(44, 36, 168, 24 + (_c.Pending?.Options.Count ?? 0) * 11 + 13);

    private Rectangle ReactionRect(int i) => new(ReactionBox.X + 10, ReactionBox.Y + 22 + i * 11, ReactionBox.Width - 20, ButtonH);

    // ================================================================== drawing

    public void Draw(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        DrawTopBar(b, c);
        foreach (var h in _c.Heroes) DrawHero(b, c, s, h);
        foreach (var e in _c.Enemies) DrawEnemy(b, c, s, e);
        DrawInfo(b, c);

        if (_c.ActiveHero is not null && !_c.IsOver) DrawHand(b, c, _c.ActiveHero);
        else if (!_c.IsOver) Gui.TextCenter(b, c, "ENEMY TURN...", 128, HandY + 16, Palette.DarkGrey);

        _vfx.Draw(b, c);
        foreach (var f in _floaters)
            TinyFont.DrawShadowed(b, c, f.Text, (int)f.Pos.X, (int)f.Pos.Y, f.Color);

        if (!Busy && _c.Phase == CombatPhase.AwaitingReaction) DrawReaction(b, c);
        if (!Busy && _c.Phase == CombatPhase.AwaitingTiming && _timingFor is not null) DrawTiming(b, c, s);
        if (!Busy && _c.IsOver) DrawEndBanner(b, c);
    }

    private void DrawTopBar(SpriteBatch b, PixelCanvas c)
    {
        TinyFont.DrawShadowed(b, c, $"ROUND {_c.Round}", 4, 3, Palette.Peach);
        // Luck in green while this fight's reroll is still available, grey once it's been used.
        TinyFont.DrawShadowed(b, c, $"LUCK {_c.Party.Luck}", 42, 3, _c.LuckAvailable ? Palette.Green : Palette.DarkGrey);
        string kind = _c.Kind == CombatKind.Normal ? "" : _c.Kind.ToString().ToUpperInvariant();
        Gui.TextCenter(b, c, kind, 128, 3, Palette.Pink, shadow: true);
        Gui.TextRight(b, c, $"TORCH {_c.Party.Torch}  GOLD {_c.Party.Gold}", 252, 3, Palette.Orange, shadow: true);
        if (_c.Dark && (int)(_time * 2) % 3 != 0)
            TinyFont.DrawShadowed(b, c, "DARK: DIS", 76, 3, Palette.Lavender);
    }

    /// <summary>Cards that get the cast sparkle: spell attacks, saves, and wizard/cleric cards.</summary>
    private static bool IsSpell(CardDef card) =>
        card.Spell || card.Check == CheckKind.Save
        || card.ClassId.Equals("wizard", StringComparison.OrdinalIgnoreCase)
        || card.ClassId.Equals("cleric", StringComparison.OrdinalIgnoreCase)
        || card.Id.Equals("basic.spare_the_dying", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lunge: a quick hop of up to 4px toward the other side (heroes right, enemies left), then back.</summary>
    private int LungeOffset(Combatant who)
    {
        if (!_lunge.TryGetValue(who, out float left)) return 0;
        double progress = 1 - left / LungeTime;
        int dx = (int)Math.Round(Math.Sin(progress * Math.PI) * 4);
        return who is Hero ? dx : -dx;
    }

    /// <summary>Spell sparkle (four orbiting pixels) and shield flash (a blinking blue box) around an 8×8 sprite.</summary>
    private void DrawFx(SpriteBatch b, PixelCanvas c, Combatant who, int x, int y)
    {
        if (_cast.TryGetValue(who, out float cast))
        {
            double radius = 4 + (1 - cast / CastTime) * 5;
            for (int i = 0; i < 4; i++)
            {
                double angle = _time * 9 + i * Math.PI / 2;
                int px = x + 4 + (int)Math.Round(Math.Cos(angle) * radius);
                int py = y + 4 + (int)Math.Round(Math.Sin(angle) * radius);
                c.Fill(b, px, py, 1, 1, i % 2 == 0 ? Palette.Yellow : Palette.White);
            }
        }
        if (_shieldFx.TryGetValue(who, out float shield) && (int)(shield * 20) % 2 == 0)
            c.Outline(b, new Rectangle(x - 2, y - 2, 12, 12), Palette.Blue);
    }

    private void DrawHero(SpriteBatch b, PixelCanvas c, SpriteSheet s, Hero h)
    {
        var p = PosOf(h);
        int hp = Shown(h);
        bool active = ReferenceEquals(_c.ActiveHero, h);
        bool flashing = _flash.ContainsKey(h) && (int)(_time * 20) % 2 == 0;
        bool down = h.IsDowned || (hp == 0 && !h.IsDead);

        int lunge = LungeOffset(h);
        var tint = down ? Palette.DarkGrey : _cast.ContainsKey(h) && (int)(_time * 12) % 2 == 0 ? Palette.Peach : Color.White;
        string sprite = SpriteFor(s, h);
        if (_cast.ContainsKey(h) && s.Has(sprite + ".cast")) sprite += ".cast";
        else if (_lunge.ContainsKey(h) && s.Has(sprite + ".atk")) sprite += ".atk";
        if (h.IsDead) s.Draw(b, "i.skull", p.X, p.Y);
        else if (!flashing) s.Draw(b, sprite, p.X + lunge, p.Y, tint);
        DrawFx(b, c, h, p.X + lunge, p.Y);

        if (active && !Busy && (int)(_time * 3) % 2 == 0)
            TinyFont.Draw(b, c, ">", p.X - 5, p.Y + 2, Palette.Yellow);

        DrawTargetMarker(b, c, h, p);

        var nameCol = h.IsDead ? Palette.DarkGrey : active ? Palette.Yellow : Palette.White;
        TinyFont.DrawShadowed(b, c, Gui.Clip(h.Name.ToUpperInvariant(), 9), p.X + 11, p.Y, nameCol);
        Gui.HpBar(b, c, p.X + 11, p.Y + 7, 40, hp, h.MaxHp);

        if (h.IsDead) TinyFont.Draw(b, c, "DEAD", p.X + 11, p.Y + 12, Palette.DarkGrey);
        else if (down) TinyFont.Draw(b, c, $"DOWN {h.DeathSaveSuccesses}/{h.DeathSaveFailures}", p.X + 11, p.Y + 12, Palette.Red);
        else TinyFont.Draw(b, c, $"{hp}/{h.MaxHp}", p.X + 11, p.Y + 12, Palette.LightGrey);

        if (h.Block > 0) TinyFont.Draw(b, c, $"[{h.Block}]", p.X + 35, p.Y + 12, Palette.Blue);
        DrawConditions(b, c, h, p.X + 52, p.Y);
    }

    private void DrawEnemy(SpriteBatch b, PixelCanvas c, SpriteSheet s, Enemy e)
    {
        int hp = Shown(e);
        if (hp <= 0 && !e.IsAlive) return;

        var p = PosOf(e);
        bool flashing = _flash.ContainsKey(e) && (int)(_time * 20) % 2 == 0;
        int lunge = LungeOffset(e);
        if (!flashing) s.Draw(b, SpriteFor(s, e), p.X + lunge, p.Y, flipX: true);
        DrawFx(b, c, e, p.X + lunge, p.Y);
        DrawTargetMarker(b, c, e, p);

        string name = Gui.Clip(e.Name.ToUpperInvariant(), 11);
        Gui.TextRight(b, c, name, p.X - 3, p.Y, Palette.White, shadow: true);
        int barX = p.X - 3 - 40;
        Gui.HpBar(b, c, barX, p.Y + 6, 40, hp, e.MaxHp);
        Gui.TextRight(b, c, hp.ToString(), barX - 2, p.Y + 5, Palette.LightGrey);
        if (e.Block > 0) Gui.TextRight(b, c, $"[{e.Block}]", p.X - 6 - TinyFont.Measure(name), p.Y, Palette.Blue);
        DrawConditions(b, c, e, InfoX + InfoW + 4, p.Y);

        var intent = _c.IntentFor(e);
        if (intent is null || _c.IsOver) return;
        string text;
        Color col;
        string icon;
        if (intent.Intent.Kind == IntentKind.Guard)
        {
            text = $"GUARD {intent.Intent.Block}";
            col = Palette.Blue;
            icon = "i.guard";
        }
        else
        {
            string target = intent.Target is null ? "?" : Gui.Clip(intent.Target.Name.ToUpperInvariant(), 8);
            text = intent.Odds is { } o ? $"{target} {o.Percent}%" : target;
            col = Palette.Red;
            icon = "i.attack";
        }
        Gui.TextRight(b, c, text, 252, p.Y + 12, col);
        s.Draw(b, icon, 252 - TinyFont.Measure(text) - 10, p.Y + 10);
        if (intent.Intent.Big && (int)(_time * 4) % 4 != 0)
            s.Draw(b, "i.bolt", 252 - TinyFont.Measure(text) - 19, p.Y + 10);
    }

    private static string SpriteFor(SpriteSheet s, Combatant c)
    {
        string name = c switch
        {
            Hero h => "hero." + h.Class.Id,
            Enemy e => "enemy." + e.Def.Id,
            _ => ""
        };
        return s.Has(name) ? name : c is Hero ? "hero" : "fight";
    }

    private void DrawTargetMarker(SpriteBatch b, PixelCanvas c, Combatant t, Point p)
    {
        if (!Targeting || Busy) return;
        var side = CurrentSide();
        var valid = ValidTargets(side);
        int index = valid.IndexOf(t);
        if (index < 0) return;

        bool hover = ReferenceEquals(_hoverTarget, t);
        if (hover || (int)(_time * 4) % 2 == 0)
            c.Outline(b, new Rectangle(p.X - 2, p.Y - 2, 12, 12), hover ? Palette.Green : Palette.Yellow);
        int numberX = t is Hero ? p.X - 5 : p.X + 10;
        TinyFont.Draw(b, c, (index + 1).ToString(), numberX, p.Y + 2, Palette.Yellow);
    }

    private static void DrawConditions(SpriteBatch b, PixelCanvas c, Combatant who, int x, int y)
    {
        foreach (string cond in who.Conditions.Active)
        {
            var col = cond switch
            {
                Cond.Blessed => Palette.Yellow,
                Cond.Taunting => Palette.Red,
                Cond.Marked => Palette.Pink,
                _ => Palette.Lavender
            };
            TinyFont.Draw(b, c, cond[..1].ToUpperInvariant(), x, y, col);
            x += 5;
        }
    }

    private void DrawInfo(SpriteBatch b, PixelCanvas c)
    {
        var box = new Rectangle(InfoX, InfoY, InfoW, InfoH);
        Gui.Panel(b, c, box, Palette.Black, Palette.DarkBlue);
        int maxLines = (InfoH - 4 - (_strip.HasContent ? RollStrip.Height : 0)) / 7;
        DrawStrip(b, c);

        var card = _selected ?? _hoverCard;
        if (card is not null && _c.ActiveHero is not null && !Busy)
        {
            var lines = new List<(string, Color)>
            {
                (card.DisplayName.ToUpperInvariant(), card.Upgraded ? Palette.Green : Palette.Yellow),
                ($"{card.Def.Speed} {(card.Def.Check == CheckKind.None ? "" : card.Def.Check.ToString())}".ToUpperInvariant().Trim(), SpeedColor(card.Def.Speed)),
            };
            lines.AddRange(Gui.Wrap(card.Def.Text.ToUpperInvariant(), InfoChars).Select(l => (l, Palette.White)));
            DrawLines(b, c, lines, maxLines);
            return;
        }

        int start = Math.Max(0, _log.Count - maxLines);
        DrawLines(b, c, _log.Skip(start).ToList(), maxLines);
    }

    private void DrawStrip(SpriteBatch b, PixelCanvas c)
    {
        bool luck = !Busy && _c.Phase == CombatPhase.AwaitingLuck && _c.PendingLuck is not null;
        if (!luck)
        {
            _strip.Draw(b, c);
            return;
        }
        _strip.Draw(b, c, $"MISS! REROLL FOR 1 LUCK? ({_c.Party.Luck})", hideLastRow: true);
        Gui.Button(b, c, LuckYesRect, "Y  REROLL", LuckYesRect.Contains(_pointer), true, Palette.Green);
        Gui.Button(b, c, LuckNoRect, "N  KEEP", LuckNoRect.Contains(_pointer));
    }

    private void DrawTiming(SpriteBatch b, PixelCanvas c, SpriteSheet s)
    {
        var p = _timingFor!;
        var box = new Rectangle(36, 28, 184, 50);
        Gui.Panel(b, c, box, Palette.Black, Palette.Red);
        s.Draw(b, "i.bolt", box.X + 4, box.Y + 3);
        s.Draw(b, "i.bolt", box.Right - 12, box.Y + 3);
        Gui.TextCenter(b, c, Gui.Clip($"{p.Attacker.Name} {p.Intent.Name} > {p.Defender.Name}!".ToUpperInvariant(), 40), box.Center.X, box.Y + 5, Palette.Red, shadow: true);

        string sub;
        Color subColor;
        if (_timingGrade is TimingGrade g)
        {
            sub = g switch
            {
                TimingGrade.Perfect => "PERFECT! DODGED",
                TimingGrade.Good => "GOOD! HALF DAMAGE",
                TimingGrade.Ok => "OK. 25% LESS DAMAGE",
                _ => "MISSED! FULL DAMAGE"
            };
            subColor = GradeColor(g);
        }
        else if (_timingT < TimingReady)
        {
            sub = "GET READY...";
            subColor = Palette.LightGrey;
        }
        else
        {
            sub = "PRESS SPACE IN THE ZONE!";
            subColor = (int)(_time * 6) % 2 == 0 ? Palette.White : Palette.Yellow;
        }
        Gui.TextCenter(b, c, sub, box.Center.X, box.Y + 14, subColor);

        // The bar: Ok (orange) ⊃ Good (green) ⊃ Perfect (yellow), centred on a random spot.
        var bar = new Rectangle(box.X + 12, box.Y + 26, box.Width - 24, 7);
        c.Fill(b, bar, Palette.DarkBlue);
        double w = p.Window;
        Zone(b, c, bar, _timingCenter, w, Palette.Orange);
        Zone(b, c, bar, _timingCenter, w / 2, Palette.Green);
        Zone(b, c, bar, _timingCenter, w / 6, Palette.Yellow);
        c.Outline(b, bar, Palette.LightGrey);

        double marker = _timingGrade is null ? TimingMarker : Math.Clamp((_timingT - TimingReady) / TimingSweep, 0f, 1f);
        int mx = bar.X + (int)Math.Round(marker * (bar.Width - 1));
        c.Fill(b, mx, bar.Y - 3, 1, bar.Height + 6, Palette.White);
        c.Fill(b, mx - 1, bar.Y - 3, 3, 1, Palette.White);

        Gui.TextCenter(b, c, "YELLOW DODGE  GREEN HALF  ORANGE -25%", box.Center.X, box.Bottom - 9, Palette.DarkGrey);
    }

    /// <summary>A zone of half-width <paramref name="halfWidth"/> around <paramref name="center"/>, clipped to the bar.</summary>
    private static void Zone(SpriteBatch b, PixelCanvas c, Rectangle bar, double center, double halfWidth, Color color)
    {
        int x0 = bar.X + (int)Math.Round(Math.Clamp(center - halfWidth, 0, 1) * bar.Width);
        int x1 = bar.X + (int)Math.Round(Math.Clamp(center + halfWidth, 0, 1) * bar.Width);
        c.Fill(b, x0, bar.Y + 1, Math.Max(1, x1 - x0), bar.Height - 2, color);
    }

    private static void DrawLines(SpriteBatch b, PixelCanvas c, IReadOnlyList<(string Text, Color Color)> lines, int maxLines)
    {
        int y = InfoY + 3;
        foreach (var (text, color) in lines.Take(maxLines))
        {
            TinyFont.Draw(b, c, Gui.Clip(text, InfoChars), InfoX + 3, y, color);
            y += 7;
        }
    }

    private void DrawHand(SpriteBatch b, PixelCanvas c, Hero hero)
    {
        // Prompt line above the hand
        string prompt = _hint;
        if (Busy) prompt = "";
        else if (Targeting)
        {
            string what = AimedName.ToUpperInvariant();
            prompt = $"{what}: PICK A TARGET";
            var aimed = AimedCard;
            if (_hoverTarget is not null && aimed is not null)
            {
                bool attack = aimed.Check == CheckKind.Attack;
                bool save = aimed.Check == CheckKind.Save;
                if (attack && _hoverTarget is Enemy) prompt = $"{what} > {_hoverTarget.Name.ToUpperInvariant()}: HIT {_c.PreviewAttack(hero, _hoverTarget, aimed)}";
                else if (save && _hoverTarget is Enemy en) prompt = $"{what} > {en.Name.ToUpperInvariant()}: FAILS SAVE {(int)Math.Round(_c.PreviewSaveFail(hero, en) * 100)}%";
            }
        }
        else if (prompt.Length == 0) prompt = $"{hero.Name.ToUpperInvariant()}'S TURN";
        TinyFont.DrawShadowed(b, c, Gui.Clip(prompt, 41), 4, HandY - 9, Targeting ? Palette.Green : Palette.Peach);
        Gui.TextRight(b, c, $"DECK {hero.Deck.DrawPile.Count} DISC {hero.Deck.Discard.Count}", 252, HandY - 9, Palette.DarkGrey);

        var hand = hero.Deck.Hand;
        for (int i = 0; i < Math.Min(hand.Count, MaxCards); i++)
            DrawCard(b, c, hand[i], CardRect(i, hand[i]), i);
        if (hand.Count == 0)
            TinyFont.Draw(b, c, "NO CARDS: USE STRIKE OR GUARD", 4, HandY + 16, Palette.DarkGrey);

        bool idle = !Busy && _c.Phase == CombatPhase.HeroTurn;
        var strike = _c.Content.Card(ContentDb.BasicStrikeId);
        Gui.Button(b, c, StrikeButton, "S STRIKE", idle && StrikeButton.Contains(_pointer), idle && _c.CanUseBasic(strike),
            ReferenceEquals(_basicSelected, strike) ? Palette.Yellow : null);
        Gui.Button(b, c, GuardButton, "G GUARD", idle && GuardButton.Contains(_pointer), idle && _c.CanUseBasic());
        var basic = _c.ClassBasic(hero);
        string basicLabel = basic is null ? "C -" : "C " + (basic.Short ?? Gui.Clip(basic.Name.ToUpperInvariant(), 7)).ToUpperInvariant();
        Gui.Button(b, c, BasicButton, basicLabel, idle && BasicButton.Contains(_pointer), idle && basic is not null && _c.CanUseBasic(basic),
            basic is not null && ReferenceEquals(_basicSelected, basic) ? Palette.Yellow : basic?.Speed == CardSpeed.Quick ? Palette.Green : Palette.Blue);
        int items = _c.Party.Bag.Count;
        string? firstItem = _c.Party.Bag.FirstOrDefault();
        Gui.Button(b, c, ItemButton, $"B BAG {items}", idle && ItemButton.Contains(_pointer), idle && firstItem is not null && _c.CanUseItem(firstItem),
            _itemSelected is not null ? Palette.Yellow : Palette.Green);
        Gui.Button(b, c, EndButton, "E  END TURN", idle && EndButton.Contains(_pointer), idle, Palette.Green);
        bool canFlee = _c.CanFlee(out _);
        Gui.Button(b, c, FleeButton, canFlee ? $"F  FLEE -{_c.Content.Balance.FleeTorchCost} TORCH" : "F  NO ESCAPE", idle && FleeButton.Contains(_pointer), idle && canFlee);
    }

    private void DrawCard(SpriteBatch b, PixelCanvas c, CardInstance card, Rectangle r, int index)
    {
        bool playable = !Busy && _c.CanPlay(card);
        bool selected = ReferenceEquals(card, _selected);
        bool hover = ReferenceEquals(card, _hoverCard);
        var border = selected ? Palette.Yellow : hover ? Palette.White : playable ? SpeedColor(card.Def.Speed) : Palette.DarkGrey;
        var fill = card.Def.Speed switch
        {
            CardSpeed.Quick => Palette.DarkGreen,
            CardSpeed.Reaction => Palette.DarkPurple,
            _ => Palette.DarkBlue
        };
        Gui.Panel(b, c, r, playable || selected ? fill : Palette.Black, border);

        TinyFont.Draw(b, c, (index + 1).ToString(), r.X + 2, r.Y + 2, Palette.LightGrey);
        string speed = card.Def.Speed switch { CardSpeed.Quick => "Q", CardSpeed.Reaction => "R", _ => "A" };
        TinyFont.Draw(b, c, speed, r.Right - 5, r.Y + 2, SpeedColor(card.Def.Speed));

        int y = r.Y + 10;
        foreach (string line in Gui.Wrap(card.ToString().ToUpperInvariant(), 7).Take(3))
        {
            Gui.TextCenter(b, c, line, r.Center.X, y, playable ? Palette.White : Palette.LightGrey);
            y += 7;
        }

        string footer = card.Def.Check switch
        {
            CheckKind.Attack => "ATTACK",
            CheckKind.Save => "SAVE",
            _ => card.Def.Speed switch { CardSpeed.Quick => "QUICK", CardSpeed.Reaction => "REACT", _ => "SKILL" }
        };
        Gui.TextCenter(b, c, footer, r.Center.X, r.Bottom - 7, Palette.LightGrey);
    }

    private static Color SpeedColor(CardSpeed speed) => speed switch
    {
        CardSpeed.Quick => Palette.Green,
        CardSpeed.Reaction => Palette.Pink,
        _ => Palette.Blue
    };

    private void DrawReaction(SpriteBatch b, PixelCanvas c)
    {
        var p = _c.Pending!;
        var box = ReactionBox;
        Gui.Panel(b, c, box, Palette.Black, Palette.Yellow);
        Gui.TextCenter(b, c, $"{p.Attacker.Name.ToUpperInvariant()} HITS {p.Defender.Name.ToUpperInvariant()}!", box.Center.X, box.Y + 4, Palette.White);
        Gui.TextCenter(b, c, $"ROLLED {p.Result.Total} VS AC {p.Result.Target}. REACT?", box.Center.X, box.Y + 12, Palette.LightGrey);

        for (int i = 0; i <= p.Options.Count; i++)
        {
            var r = ReactionRect(i);
            string label = i < p.Options.Count
                ? (i == 0 ? "Y  " : "   ") + Gui.Clip($"{p.Options[i].Def.Name}: {p.Options[i].Def.Text}".ToUpperInvariant(), 34)
                : "N  TAKE THE HIT";
            Gui.Button(b, c, r, label, r.Contains(_pointer), true, i < p.Options.Count ? Palette.Pink : null);
        }
    }

    private void DrawEndBanner(SpriteBatch b, PixelCanvas c)
    {
        var (title, color, sub) = _c.Phase switch
        {
            CombatPhase.Victory => ("VICTORY!", Palette.Yellow, $"+{_c.GoldEarned} GOLD  +{_c.XpEarned} XP"),
            CombatPhase.Defeat => ("DEFEAT", Palette.Red, "THE PARTY HAS FALLEN"),
            _ => ("ESCAPED", Palette.LightGrey, "BACK THE WAY YOU CAME")
        };
        var box = new Rectangle(64, 38, 128, 40);
        Gui.Panel(b, c, box, Palette.Black, color);
        Gui.TextCenter(b, c, title, box.Center.X, box.Y + 7, color, shadow: true);
        Gui.TextCenter(b, c, sub, box.Center.X, box.Y + 17, Palette.White);
        if ((int)(_time * 2) % 2 == 0)
            Gui.TextCenter(b, c, "CLICK TO CONTINUE", box.Center.X, box.Y + 29, Palette.DarkGrey);
    }
}
