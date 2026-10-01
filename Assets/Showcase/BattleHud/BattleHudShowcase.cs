using System.Collections.Generic;
using UnityEngine;

namespace Yaui.Showcase
{
    /// <summary>
    /// A raid battle HUD over a 3D scene, built in code at startup and driven by a fake fight: unit frames, raid
    /// frames, a boss bar, action bars with cooldowns, a minimap, a quest tracker, a chat log, a damage meter,
    /// hundreds of floating damage numbers, world space nameplates, particles and custom shaders.
    /// </summary>
    public sealed partial class BattleHudShowcase : MonoBehaviour
    {
        [SerializeField] Camera worldCamera;
        [SerializeField] Shader liquidShader;
        [SerializeField] Shader sheenShader;
        [SerializeField] Shader particleShader;
        [SerializeField] Shader backdropShader;

        /// <summary>Hits per second dealt by the raid.</summary>
        [SerializeField] float hitRate = 38f;

        static class Pal
        {
            public static readonly Color Text = Ui.Hex(0xE8EEFF);
            public static readonly Color Dim = Ui.Hex(0x93A0C4);
            public static readonly Color Cyan = Ui.Hex(0x4DD8FF);
            public static readonly Color Gold = Ui.Hex(0xFFC857);
            public static readonly Color Red = Ui.Hex(0xFF4D6D);
            public static readonly Color Green = Ui.Hex(0x5CE1A0);
            public static readonly Color Blue = Ui.Hex(0x4C8DFF);
            public static readonly Color Purple = Ui.Hex(0xB388FF);
            public static readonly Color Orange = Ui.Hex(0xFF8A3D);
            public static readonly Color Ink = Ui.Hex(0x0B1020);
            public static readonly Color[] Classes = { Blue, Green, Red, Purple, Orange, Cyan, Gold };

            public static BoxStyle Panel => Sty.Fill(Ui.Hex(0x0B1020, 0.8f)).Radius(12f)
                .Border(1f, Ui.Hex(0x7FA8FF, 0.24f))
                .Shadow(new Color(0f, 0f, 0f, 0.5f), 12f, 0f, new Vector2(0f, 6f));
        }

        enum NumberKind
        {
            Normal,
            Crit,
            Huge,
            Heal,
            Hurt
        }

        sealed class DamageNumber
        {
            public YauiText Text;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Age;
            public float Life;
            public float Size;
            public bool Active;
        }

        sealed class Skill
        {
            public YauiElement Slot;
            public RadialElement Sweep;
            public YauiText Label;
            public YauiElement Sheen;
            public Color Color;
            public float Cooldown;
            public float Remaining;
            public float Hold;
            public float Pop;
            public int Shown = -1;
        }

        sealed class Buff
        {
            public YauiElement Slot;
            public RadialElement Sweep;
            public YauiText Label;
            public float Duration;
            public float Remaining;
            public float Pop;
            public int Shown = -1;
        }

        sealed class Member
        {
            public string Name;
            public Color Color;
            public Bar Hp;
            public float Health = 1f;
            public float Total;
            public float Rate;
        }

        sealed class MeterRow
        {
            public YauiElement Fill;
            public YauiText Name;
            public YauiText Value;
            public float Shown = -1f;
        }

        Material _liquid;
        Material _sheen;
        Sprite[] _icons;
        YauiPanel _panel;
        YauiElement _root;

        readonly List<DamageNumber> _numbers = new();
        int _nextNumber;
        ParticleSystem _sparks;

        readonly List<Skill> _skills = new();
        readonly List<Buff> _buffs = new();
        readonly List<Buff> _debuffs = new();
        readonly List<Member> _members = new();
        readonly List<MeterRow> _meter = new();
        readonly List<Member> _ranking = new();

        Bar _playerHp;
        Bar _playerMp;
        YauiText _playerHpLabel;
        YauiText _playerMpLabel;
        float _health = 0.86f;
        float _mana = 0.7f;

        Bar _bossHp;
        Bar _bossCast;
        YauiText _bossHpLabel;
        YauiText _bossPercent;
        YauiText _bossPhase;
        YauiText _bossCastLabel;
        float _bossCastTime;
        float _bossCastDuration = 4f;
        int _bossSpell;

        Bar _cast;
        YauiText _castLabel;
        YauiElement _castFrame;
        float _castTime = -1f;
        float _castIdle = 0.5f;
        int _spell;

        YauiElement _ult;
        RadialElement _ultRing;
        YauiText _ultLabel;
        float _charge;

        YauiElement _combo;
        YauiText _comboNumber;
        int _hits;
        int _hitsShown = -1;
        float _comboPop;
        float _comboIdle;

        RadialElement _radar;
        readonly List<YauiElement> _mapDots = new();

        readonly List<YauiText> _chat = new();
        float _chatTimer;
        int _chatIndex;

        readonly List<YauiText> _objectives = new();
        readonly List<YauiElement> _checks = new();
        int _kills;
        int _shards;
        Bar _xp;
        float _experience = 0.37f;

        YauiText _stats;
        YauiText _statsDetail;
        readonly List<YauiPanel> _panels = new();
        YauiText _meterTitle;
        float _slowTimer;
        float _fightTime;
        float _hitBudget;
        float _smoothedDelta = 1f / 60f;

        static readonly string[] Spells =
            { "Cascade Lance", "Flexbox Flurry", "Single Draw", "Glyph Storm", "Layout Boundary", "Atlas Nova" };

        static readonly string[] BossSpells = { "Cataclysm", "Overdraw", "Rebuild Canvas", "Batch Breaker" };

        void Start()
        {
            Random.InitState(20261001);
            _liquid = new Material(Find(liquidShader, "Yaui/Showcase/Liquid"));
            _sheen = new Material(Find(sheenShader, "Yaui/Showcase/Sheen"));
            _icons = IconFactory.CreateAll();

            BuildWorld();
            BuildHud();
        }

        static Shader Find(Shader shader, string name)
        {
            return shader != null ? shader : Shader.Find(name);
        }

        void Update()
        {
            var dt = Time.deltaTime;
            _fightTime += dt;
            _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);

            UpdateWorld(dt);

            _hitBudget += dt * hitRate;
            while (_hitBudget >= 1f)
            {
                _hitBudget -= 1f;
                RaidHit();
            }

            UpdateSkills(dt);
            UpdateBuffs(_buffs, dt, 8f, 30f);
            UpdateBuffs(_debuffs, dt, 5f, 18f);
            UpdateCasts(dt);
            UpdateVitals(dt);
            UpdateNumbers(dt);
            UpdateCombo(dt);
            UpdateChat(dt);
            UpdateMinimap(dt);

            _slowTimer -= dt;
            if (_slowTimer <= 0f)
            {
                _slowTimer = 0.25f;
                UpdateLabels();
                UpdateMeter();
                UpdateStats();
            }
        }

        void LateUpdate()
        {
            UpdateNameplates(Time.deltaTime);
        }

        #region Fight

        Member RandomMember()
        {
            // The first members deal more of the hits.
            var t = Random.value;
            return _members[Mathf.Min((int)(t * Mathf.Sqrt(t) * _members.Count), _members.Count - 1)];
        }

        void RaidHit()
        {
            var enemy = Random.value < 0.4f ? _enemies[0] : _enemies[Random.Range(1, _enemies.Count)];
            var crit = Random.value < 0.18f;
            var amount = Random.Range(2400f, 9800f) * (crit ? 2.6f : 1f);
            Hit(enemy, RandomMember(), amount, crit ? NumberKind.Crit : NumberKind.Normal);
        }

        void Hit(Enemy enemy, Member source, float amount, NumberKind kind)
        {
            if (enemy.Respawn > 0f) return;

            enemy.Health -= amount;
            enemy.Flash = enemy.Boss ? 0.25f : 1f;
            source.Total += amount;
            source.Rate += amount;
            _hits++;
            _comboPop = 1f;
            _comboIdle = 0f;
            if (kind != NumberKind.Huge) _charge = Mathf.Min(_charge + amount / 2500000f, 1f);

            // The top of the boss is behind its frame.
            var at = enemy.Transform.position +
                     Vector3.up * enemy.Height * (enemy.Boss ? Random.Range(-0.35f, 0.1f) : Random.Range(0f, 0.4f)) +
                     Random.insideUnitSphere * enemy.Height * 0.3f;
            if (TryCanvasPoint(at, out var canvas))
            {
                SpawnNumber(canvas, amount, kind);
                EmitSparks(canvas, kind);
            }

            if (enemy.Health <= 0f) Kill(enemy);
        }

        void Kill(Enemy enemy)
        {
            if (enemy.Boss)
            {
                enemy.Health = enemy.MaxHealth;
                PostChat($"{Ui.Tag(Pal.Gold)}[System]</color> {enemy.Name} rises again. Nothing was rebuilt.");
                return;
            }

            enemy.Respawn = Random.Range(1.2f, 2.5f);
            _kills++;
            _shards += Random.Range(0, 3);
            _experience += 0.011f;
            if (_experience >= 1f)
            {
                _experience -= 1f;
                PostChat($"{Ui.Tag(Pal.Gold)}[System] You reached level {88 + _kills / 90}!</color>");
            }
        }

        bool TryCanvasPoint(Vector3 world, out Vector2 canvas)
        {
            canvas = default;
            var screen = _camera.WorldToScreenPoint(world);
            return screen.z > 0f && _panel.TryScreenToCanvas(screen, out canvas);
        }

        void SpawnNumber(Vector2 at, float amount, NumberKind kind)
        {
            var n = _numbers[_nextNumber];
            _nextNumber = (_nextNumber + 1) % _numbers.Count;

            var value = Mathf.RoundToInt(amount).ToString("N0");
            var text = n.Text;
            switch (kind)
            {
                case NumberKind.Crit:
                    text.Text = "<b>" + value + "!</b>";
                    text.Color = Pal.Gold;
                    n.Size = 44f;
                    break;
                case NumberKind.Huge:
                    text.Text = "<b>" + value + "!!</b>";
                    text.Color = Pal.Orange;
                    n.Size = 60f;
                    break;
                case NumberKind.Heal:
                    text.Text = "+" + value;
                    text.Color = Pal.Green;
                    n.Size = 32f;
                    break;
                case NumberKind.Hurt:
                    text.Text = "<b>-" + value + "</b>";
                    text.Color = Pal.Red;
                    n.Size = 38f;
                    break;
                default:
                    text.Text = value;
                    text.Color = Color.white;
                    n.Size = 28f;
                    break;
            }

            text.FontSize = n.Size;
            text.OutlineWidth = kind == NumberKind.Normal ? 0f : n.Size * 0.06f;
            n.Position = at;
            n.Velocity = new Vector2(Random.Range(-90f, 90f), Random.Range(-330f, -210f));
            n.Age = 0f;
            n.Life = kind == NumberKind.Normal ? 0.9f : 1.25f;
            n.Active = true;
        }

        void UpdateNumbers(float dt)
        {
            foreach (var n in _numbers)
            {
                if (!n.Active) continue;

                n.Age += dt;
                var t = n.Age / n.Life;
                if (t >= 1f)
                {
                    n.Active = false;
                    // A collapsed quad costs no pixels.
                    n.Text.Scale = Vector2.zero;
                    continue;
                }

                n.Velocity.y += 420f * dt;
                n.Velocity.x *= 1f - 1.5f * dt;
                n.Position += n.Velocity * dt;

                // Transforms and opacity never run the layout.
                var pop = 1f + 0.9f * Mathf.Exp(-n.Age * 16f);
                n.Text.Translate = n.Position - NumberSize * 0.5f;
                n.Text.Scale = new Vector2(pop, pop);
                n.Text.Opacity = 1f - Mathf.InverseLerp(0.6f, 1f, t);
            }
        }

        void EmitSparks(Vector2 canvas, NumberKind kind)
        {
            var size = _panel.CanvasSize;
            // The systems simulate around the center of the element, with Y up.
            var position = new Vector3(canvas.x - size.x * 0.5f, size.y * 0.5f - canvas.y, 0f);
            var count = kind == NumberKind.Normal ? 3 : kind == NumberKind.Huge ? 22 : 10;
            var color = kind switch
            {
                NumberKind.Normal => Pal.Cyan,
                NumberKind.Heal => Pal.Green,
                NumberKind.Hurt => Pal.Red,
                NumberKind.Huge => Pal.Orange,
                _ => Pal.Gold
            };
            for (var i = 0; i < count; i++)
            {
                var direction = Random.insideUnitCircle.normalized;
                _sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = direction * Random.Range(160f, 620f) + Vector2.up * 120f,
                    startSize = Random.Range(8f, 20f),
                    startLifetime = Random.Range(0.25f, 0.6f),
                    startColor = Color.Lerp(color, Color.white, Random.Range(0f, 0.6f))
                }, 1);
            }
        }

        void UpdateSkills(float dt)
        {
            foreach (var s in _skills)
            {
                if (s.Remaining > 0f)
                {
                    s.Remaining -= dt;
                    if (s.Remaining <= 0f)
                    {
                        s.Remaining = 0f;
                        s.Hold = Random.Range(0.4f, 3f);
                        s.Sheen.Scale = Vector2.one;
                    }
                }
                else
                {
                    s.Hold -= dt;
                    if (s.Hold <= 0f) Use(s);
                }

                // The dark sector shrinks clockwise from the top.
                var r = s.Remaining / s.Cooldown;
                s.Sweep.SetArc(-90f + 360f * (1f - r), 360f * r);
                var seconds = Mathf.CeilToInt(s.Remaining);
                if (seconds != s.Shown)
                {
                    s.Shown = seconds;
                    s.Label.Text = seconds > 0 ? seconds.ToString() : "";
                }

                if (s.Pop > 0f)
                {
                    s.Pop = Mathf.Max(s.Pop - dt * 5f, 0f);
                    var scale = 1f + 0.22f * s.Pop * s.Pop;
                    s.Slot.Scale = new Vector2(scale, scale);
                }
            }

            _charge = Mathf.Min(_charge + dt * 0.02f, 1f);
            _ultRing.SetArc(-90f, 360f * _charge);
            var pulse = _charge >= 1f ? 1f + 0.06f * Mathf.Sin(Time.time * 9f) : 1f;
            _ult.Scale = new Vector2(pulse, pulse);
            if (_charge >= 1f && Random.value < dt * 0.6f) Ultimate();
        }

        void Use(Skill skill)
        {
            skill.Remaining = skill.Cooldown;
            skill.Pop = 1f;
            skill.Sheen.Scale = Vector2.zero;
            _mana = Mathf.Max(_mana - 0.05f, 0.05f);
            var targets = Random.Range(1, 4);
            for (var i = 0; i < targets; i++)
                Hit(_enemies[Random.Range(0, _enemies.Count)], _members[0], Random.Range(5000f, 12000f),
                    NumberKind.Crit);
        }

        void Ultimate()
        {
            _charge = 0f;
            PostChat($"{Ui.Tag(Pal.Cyan)}[Raid] Ruccho</color> unleashes {Ui.Tag(Pal.Gold)}<b>One Draw Call</b></color>!");
            foreach (var enemy in _enemies)
                for (var i = 0; i < 2; i++)
                    Hit(enemy, _members[0], Random.Range(21000f, 48000f), NumberKind.Huge);
        }

        void UpdateBuffs(List<Buff> buffs, float dt, float minDuration, float maxDuration)
        {
            foreach (var b in buffs)
            {
                b.Remaining -= dt;
                if (b.Remaining <= 0f)
                {
                    b.Duration = b.Remaining = Random.Range(minDuration, maxDuration);
                    b.Pop = 1f;
                }

                // The dark sector grows clockwise as the time runs out.
                b.Sweep.SetArc(-90f, 360f * (1f - b.Remaining / b.Duration));
                if (b.Label != null)
                {
                    var seconds = Mathf.CeilToInt(b.Remaining);
                    if (seconds != b.Shown)
                    {
                        b.Shown = seconds;
                        b.Label.Text = seconds.ToString();
                    }
                }

                if (b.Pop > 0f)
                {
                    b.Pop = Mathf.Max(b.Pop - dt * 4f, 0f);
                    var scale = 1f + 0.35f * b.Pop * b.Pop;
                    b.Slot.Scale = new Vector2(scale, scale);
                }
            }
        }

        void UpdateCasts(float dt)
        {
            // The player's casts.
            if (_castTime < 0f)
            {
                _castIdle -= dt;
                if (_castIdle <= 0f)
                {
                    _castTime = 0f;
                    _spell = (_spell + 1) % Spells.Length;
                    _castLabel.Text = Spells[_spell];
                    _castFrame.Opacity = 1f;
                }
            }
            else
            {
                _castTime += dt;
                _cast.Value = _castTime / 1.4f;
                if (_castTime >= 1.4f)
                {
                    _castTime = -1f;
                    _castIdle = Random.Range(0.5f, 1.4f);
                    _castFrame.Opacity = 0.35f;
                    _mana = Mathf.Max(_mana - 0.08f, 0.05f);
                    for (var i = 0; i < 6; i++)
                        Hit(_enemies[Random.Range(0, _enemies.Count)], _members[0], Random.Range(6000f, 14000f),
                            Random.value < 0.4f ? NumberKind.Crit : NumberKind.Normal);
                }
            }

            _cast.Tick(dt);

            // The boss's casts hurt the raid.
            _bossCastTime += dt;
            _bossCast.Value = _bossCastTime / _bossCastDuration;
            _bossCast.Tick(dt);
            if (_bossCastTime >= _bossCastDuration)
            {
                _bossCastTime = 0f;
                _bossCastDuration = Random.Range(3f, 5.5f);
                PostChat($"{Ui.Tag(Pal.Red)}[Boss] Vorathiel casts <b>{BossSpells[_bossSpell]}</b>!</color>");
                _bossSpell = (_bossSpell + 1) % BossSpells.Length;
                _bossCastLabel.Text = BossSpells[_bossSpell];

                foreach (var m in _members) m.Health = Mathf.Max(m.Health - Random.Range(0.1f, 0.55f), 0.04f);

                var damage = Random.Range(0.18f, 0.34f);
                _health = Mathf.Max(_health - damage, 0.08f);
                if (TryCanvasPoint(Vector3.up * 1.6f, out var canvas))
                {
                    SpawnNumber(canvas, damage * 84000f, NumberKind.Hurt);
                    EmitSparks(canvas, NumberKind.Hurt);
                }
            }
        }

        void UpdateVitals(float dt)
        {
            // Healers keep the raid up.
            foreach (var m in _members)
            {
                m.Health = Mathf.Min(m.Health + dt * Random.Range(0.02f, 0.2f), 1f);
                m.Hp.Value = m.Health;
                m.Hp.Tick(dt);
                m.Rate *= 1f - dt * 0.25f;
            }

            if (_health < 0.75f && Random.value < dt * 1.2f)
            {
                var heal = Random.Range(0.05f, 0.12f);
                _health = Mathf.Min(_health + heal, 1f);
                if (TryCanvasPoint(Vector3.up * 1.2f, out var canvas))
                {
                    SpawnNumber(canvas, heal * 84000f, NumberKind.Heal);
                    EmitSparks(canvas, NumberKind.Heal);
                }
            }

            _mana = Mathf.Min(_mana + dt * 0.06f, 1f);
            _playerHp.Value = _health;
            _playerMp.Value = _mana;
            _playerHp.Tick(dt);
            _playerMp.Tick(dt);

            var boss = _enemies[0];
            _bossHp.Value = boss.Health / boss.MaxHealth;
            _bossHp.Tick(dt);
            _xp.Value = _experience;
            _xp.Tick(dt);
        }

        void UpdateCombo(float dt)
        {
            _comboIdle += dt;
            if (_hits != _hitsShown)
            {
                _hitsShown = _hits;
                _comboNumber.Text = "<b><i>" + _hits.ToString("N0") + "</i></b>";
            }

            _comboPop = Mathf.Max(_comboPop - dt * 7f, 0f);
            var scale = 1f + 0.16f * _comboPop;
            _combo.Scale = new Vector2(scale, scale);
        }

        #endregion

        #region Slow updates

        void UpdateLabels()
        {
            _playerHpLabel.Text = $"{Mathf.RoundToInt(_health * 84210f):N0} / 84,210";
            _playerMpLabel.Text = $"{Mathf.RoundToInt(_mana * 31400f):N0}";

            var boss = _enemies[0];
            var fraction = boss.Health / boss.MaxHealth;
            _bossHpLabel.Text = $"{Mathf.RoundToInt(boss.Health):N0} / {Mathf.RoundToInt(boss.MaxHealth):N0}";
            _bossPercent.Text = $"<b>{fraction * 100f:0.0}%</b>";
            _bossPhase.Text = fraction > 0.66f ? "PHASE 1" : fraction > 0.33f ? "PHASE 2" : "PHASE 3 - ENRAGE";
            _ultLabel.Text = _charge >= 1f ? "<b>READY</b>" : $"{_charge * 100f:0}%";

            SetObjective(0, $"Slay invaders of the Hollow  {Mathf.Min(_kills, 60)}/60", _kills >= 60);
            SetObjective(1, $"Collect Ember Shards  {Mathf.Min(_shards, 25)}/25", _shards >= 25);
            SetObjective(2, "Defeat Vorathiel, the Unbatched", false);
            SetObjective(3, $"Survive the encounter  {(int)_fightTime / 60}:{(int)_fightTime % 60:00}", true);
        }

        void SetObjective(int index, string text, bool done)
        {
            _objectives[index].Text = text;
            _objectives[index].Color = done ? Pal.Green : Pal.Text;
            _checks[index].BackgroundColor = done ? Pal.Green : Color.clear;
        }

        void UpdateMeter()
        {
            _ranking.Clear();
            _ranking.AddRange(_members);
            _ranking.Sort((a, b) => b.Total.CompareTo(a.Total));
            var best = Mathf.Max(_ranking[0].Total, 1f);
            for (var i = 0; i < _meter.Count; i++)
            {
                var row = _meter[i];
                var m = _ranking[i];
                row.Name.Text = $"{i + 1}. {m.Name}";
                row.Value.Text = $"{m.Total / 1e6f:0.00}M  <color=#93A0C4>({m.Rate / 4000f:0.0}K)</color>";
                row.Fill.BackgroundColor = new Color(m.Color.r, m.Color.g, m.Color.b, 0.5f);
                var percent = Mathf.Round(m.Total / best * 200f) * 0.5f;
                if (percent != row.Shown)
                {
                    row.Shown = percent;
                    var layout = row.Fill.Layout;
                    layout.width = Length.Percent(percent);
                    row.Fill.Layout = layout;
                }
            }

            var seconds = (int)_fightTime;
            _meterTitle.Text = $"<b>DAMAGE DONE</b>   {seconds / 60}:{seconds % 60:00}";
        }

        readonly ShowcaseStats _counter = new();
        float _statsTimer;

        void UpdateStats()
        {
            // Counting walks every element: once a second.
            _statsTimer -= 0.25f;
            if (_statsTimer <= 0f)
            {
                _statsTimer = 1f;
                _counter.CountElements(_panels);
            }

            _counter.CountDraws(_panels);
            _stats.Text = $"<b>{_counter.Primitives:N0}</b> quads    <b>{_counter.Elements:N0}</b> elements";
            _statsDetail.Text = $"<b>{_counter.Panels}</b> panels    <b>{_counter.DrawCalls}</b> draw calls    " +
                                $"<b>{1f / _smoothedDelta:0}</b> fps";
        }

        #endregion

        #region Chat

        static readonly string[] ChatLines =
        {
            "<color=#4DD8FF>[Raid] Mira:</color> stack on the blue marker!",
            "<color=#5CE1A0>[Party] Tobi:</color> ヒールお願いします！",
            "<color=#FFC857>[Loot]</color> You receive <color=#B388FF>[Ember of the Unbatched]</color> x3",
            "<color=#4DD8FF>[Raid] Kaede:</color> バフを更新しました。次の詠唱に備えて",
            "<color=#93A0C4>[Combat]</color> Your <b>Glyph Storm</b> crits for <color=#FFC857>48,210</color>",
            "<color=#5CE1A0>[Party] Jun:</color> 탱커 뒤로 모이세요",
            "<color=#4DD8FF>[Raid] Ilse:</color> interrupt on <color=#FF4D6D>Rebuild Canvas</color> in 3... 2...",
            "<color=#FFC857>[System]</color> No canvas was rebuilt in the making of this fight.",
            "<color=#5CE1A0>[Party] Noor:</color> ممتاز، استمروا",
            "<color=#4DD8FF>[Raid] Dax:</color> adds are up, <i>cleave them down</i>",
            "<color=#93A0C4>[Combat]</color> Vorathiel's <color=#FF4D6D>Overdraw</color> hits you for 21,340",
            "<color=#4DD8FF>[Raid] Yuki:</color> レイアウトは境界で止まるから大丈夫 ✔",
            "<color=#FFC857>[Loot]</color> Bren receives <color=#4C8DFF>[Flexbox Greaves]</color>",
            "<color=#5CE1A0>[Party] Tobi:</color> mana at 20%, going easy on the heals",
            "<color=#4DD8FF>[Raid] Mira:</color> nice! phase push now, use everything"
        };

        void UpdateChat(float dt)
        {
            _chatTimer -= dt;
            if (_chatTimer > 0f) return;

            _chatTimer = Random.Range(0.5f, 1.6f);
            PostChat(ChatLines[_chatIndex]);
            _chatIndex = (_chatIndex + 1) % ChatLines.Length;
        }

        void PostChat(string line)
        {
            if (_chat.Count == 0) return;

            // The oldest line becomes the newest: a change of sibling order, picked up by the panel.
            var text = _chat[0];
            _chat.RemoveAt(0);
            _chat.Add(text);
            text.transform.SetAsLastSibling();
            var seconds = (int)_fightTime;
            text.Text = $"<color=#5A6688>{21 + seconds / 3600:00}:{47 + seconds / 60 % 13:00}</color> {line}";
        }

        #endregion
    }
}
