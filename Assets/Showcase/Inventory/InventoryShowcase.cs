using System.Collections.Generic;
using UnityEngine;

namespace Yaui.Showcase
{
    /// <summary>
    /// An RPG inventory and equipment screen, built in code at startup: a character with its equipment and stats, a
    /// scrolling grid of hundreds of item slots, and the details of the selected item. Items can be selected,
    /// equipped, enhanced and sold; left alone, the screen demonstrates itself.
    /// </summary>
    public sealed partial class InventoryShowcase : MonoBehaviour
    {
        [SerializeField] Shader holoShader;
        [SerializeField] Shader auroraShader;
        [SerializeField] Shader particleShader;

        [SerializeField] int slotCount = 352;

        /// <summary>Selects, enhances and equips items by itself until the pointer is used.</summary>
        [SerializeField] bool autoDemo = true;

        static class Pal
        {
            public static readonly Color Text = Ui.Hex(0xF2ECFF);
            public static readonly Color Dim = Ui.Hex(0x9A8FB5);
            public static readonly Color Faint = Ui.Hex(0x6C6285);
            public static readonly Color Gold = Ui.Hex(0xF5C451);
            public static readonly Color Green = Ui.Hex(0x5CE1A0);
            public static readonly Color Red = Ui.Hex(0xFF6B81);
            public static readonly Color Ink = Ui.Hex(0x120D1C);

            public static readonly Color[] Rarity =
                { Ui.Hex(0x9CA3AF), Ui.Hex(0x34D399), Ui.Hex(0x60A5FA), Ui.Hex(0xC084FC), Ui.Hex(0xFBBF24) };

            public static BoxStyle Panel => Sty.Fill(Ui.Hex(0x171024, 0.88f)).Radius(18f)
                .Border(1f, Ui.Hex(0xE9C46A, 0.22f))
                .Shadow(new Color(0f, 0f, 0f, 0.55f), 16f, 0f, new Vector2(0f, 8f));

            public static BoxStyle Inset => Sty.Fill(Ui.Hex(0x0C0814, 0.7f)).Radius(12f)
                .Border(1f, Ui.Hex(0xFFFFFF, 0.07f));
        }

        static readonly string[] RarityNames = { "Common", "Uncommon", "Rare", "Epic", "Legendary" };

        static readonly string[][] Prefixes =
        {
            new[] { "Worn", "Plain", "Rusty", "Traveler's" },
            new[] { "Sturdy", "Keen", "Polished", "Ranger's" },
            new[] { "Gleaming", "Runed", "Tempered", "Moonlit" },
            new[] { "Stormforged", "Voidtouched", "Dreadwoven", "Astral" },
            new[] { "Eternal", "Sunfire", "Unbatched", "Worldbreaker" }
        };

        // Icon, name, category, equipment slot (-1: not equipment).
        static readonly (Icon Icon, string Name, string Category, int Slot)[] Kinds =
        {
            (Icon.Sword, "Greatsword", "Weapon", 0),
            (Icon.Shield, "Bulwark", "Off-hand", 1),
            (Icon.Hexagon, "Helm", "Armor", 2),
            (Icon.Diamond, "Cuirass", "Armor", 3),
            (Icon.Cross, "Gauntlets", "Armor", 4),
            (Icon.Arrow, "Greaves", "Armor", 5),
            (Icon.Ring, "Signet", "Accessory", 6),
            (Icon.Crescent, "Amulet", "Accessory", 7),
            (Icon.Flame, "Potion", "Consumable", -1),
            (Icon.Heart, "Elixir", "Consumable", -1),
            (Icon.Bolt, "Scroll", "Consumable", -1),
            (Icon.Star, "Gemstone", "Material", -1),
            (Icon.Circle, "Ore", "Material", -1),
            (Icon.Snowflake, "Essence", "Material", -1),
            (Icon.Burst, "Rune", "Material", -1),
            (Icon.Target, "Sigil", "Material", -1)
        };

        static readonly string[] StatNames = { "Attack", "Defense", "Vitality", "Focus", "Speed" };

        static readonly string[] Flavors =
        {
            "Forged in a single pass. It never needed a second draw.",
            "「千の欠片を、ただ一度の筆で描く」— 無銘の鍛冶師",
            "Its edge was laid out once and has not moved since.",
            "They say the smith measured twice and rebuilt nothing.",
            "빛은 한 번에, 흔들림 없이.",
            "Light bends around it, but the batch does not break.",
            "古い王の持ち物。境界を越えた者はいない。"
        };

        sealed class Item
        {
            public int Kind;
            public int Rarity;
            public int Level;
            public int Count = 1;
            public int ItemLevel;
            public string Name;
            public float[] Stats;
            public int Flavor;
            public bool Equipped;
            public float Durability;

            public bool IsGear => Kinds[Kind].Slot >= 0;
        }

        sealed class Slot
        {
            public Item Item;
            public YauiElement Element;
            public YauiImage Icon;
            public YauiText Corner;
            public YauiText Count;
            public YauiElement Badge;
            public YauiElement Frame;
            public YauiElement Wear;
            public YauiElement WearFill;
            public float Pop;
        }

        sealed class Gear
        {
            public Item Item;
            public YauiElement Element;
            public YauiImage Icon;
            public YauiElement Frame;
            public float Pop;
        }

        sealed class StatRow
        {
            public Bar Bar;
            public YauiText Value;
            public float Shown = -1f;
            public float Target;
        }

        sealed class DetailStat
        {
            public YauiText Name;
            public YauiText Value;
            public YauiText Change;
        }

        readonly List<YauiPanel> _panels = new();
        readonly ShowcaseStats _counter = new();
        readonly List<Slot> _slots = new();
        readonly List<Slot> _popping = new();
        readonly List<Slot> _legendary = new();
        readonly Gear[] _gear = new Gear[8];
        readonly List<StatRow> _statRows = new();
        readonly List<DetailStat> _detailStats = new();
        readonly List<YauiElement> _pips = new();
        readonly List<RadialElement> _rings = new();
        readonly List<YauiElement> _tabs = new();
        readonly List<YauiText> _tabLabels = new();

        Sprite[] _icons;
        Material _holo;
        Material _particles;
        YauiPanel _panel;
        YauiElement _root;
        YauiScrollView _scroll;
        YauiElement _content;
        YauiElement _selection;
        YauiElement _frames;
        ParticleSystem _gridSparkles;
        ParticleSystem _burst;

        Slot _selected;
        YauiElement _detail;
        YauiElement _detailTile;
        YauiImage _detailIcon;
        YauiElement _detailFrame;
        YauiText _detailName;
        YauiText _detailType;
        YauiText _detailLevel;
        YauiText _detailEnhance;
        YauiText _detailFlavor;
        YauiText _detailNotes;
        readonly List<YauiElement> _sockets = new();
        readonly List<YauiImage> _socketIcons = new();
        YauiText _detailPrice;
        YauiText _equipLabel;
        YauiText _toast;
        YauiElement _equipButton;
        YauiElement _enhanceButton;
        float _detailFade = 1f;
        float _tilePop;
        float _toastTime = 1f;

        YauiText _power;
        YauiText _gold;
        YauiText _capacity;
        YauiText _stats;
        YauiText _statsDetail;
        float _goldShown = 128450f;
        float _goldTarget = 128450f;
        int _powerShown = -1;
        float _powerValue;
        int _category;

        float _time;
        float _idle = 99f;
        float _demoTimer = 1.5f;
        int _demoStep;
        float _scrollDirection = 1f;
        float _slowTimer;
        float _statsTimer;
        float _smoothedDelta = 1f / 60f;

        void Start()
        {
            Random.InitState(20261003);
            _icons = IconFactory.CreateAll();
            _holo = new Material(holoShader != null ? holoShader : Shader.Find("Yaui/Showcase/Holo"));
            _particles = Fx.Material(particleShader, true);
            Fx.EnsureEventSystem();

            BuildScreen();
            Select(FirstOfRarity(4));
            UpdateCharacter(true);
        }

        void Update()
        {
            var dt = Time.deltaTime;
            _time += dt;
            _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);
            _idle = Fx.PointerInUse() ? 0f : _idle + dt;

            UpdateDemo(dt);
            UpdateSelection(dt);
            UpdateAnimations(dt);
            UpdateSparkles(dt);
            UpdateCharacter(false);

            _slowTimer -= dt;
            if (_slowTimer <= 0f)
            {
                _slowTimer = 0.25f;
                _statsTimer -= 0.25f;
                if (_statsTimer <= 0f)
                {
                    _statsTimer = 1f;
                    _counter.CountElements(_panels);
                }

                _counter.CountDraws(_panels);
                _stats.Text = $"<b>{_counter.Primitives:N0}</b> quads    <b>{_counter.Elements:N0}</b> elements";
                _statsDetail.Text = $"<b>{_counter.DrawCalls}</b> draw calls    <b>{1f / _smoothedDelta:0}</b> fps";
            }
        }

        #region Items

        Item RandomItem()
        {
            var roll = Random.value;
            var rarity = roll < 0.34f ? 0 : roll < 0.6f ? 1 : roll < 0.8f ? 2 : roll < 0.93f ? 3 : 4;
            var kind = Random.value < 0.56f ? Random.Range(0, 8) : Random.Range(8, Kinds.Length);
            var prefixes = Prefixes[rarity];
            var item = new Item
            {
                Kind = kind,
                Rarity = rarity,
                Name = prefixes[Random.Range(0, prefixes.Length)] + " " + Kinds[kind].Name,
                ItemLevel = 280 + rarity * 45 + Random.Range(0, 40),
                Flavor = Random.Range(0, Flavors.Length),
                Durability = Random.Range(0.2f, 1f),
                Stats = new float[StatNames.Length]
            };
            if (item.IsGear)
            {
                item.Level = Mathf.Min(Random.Range(0, 5 + rarity * 3), 15);
                for (var i = 0; i < item.Stats.Length; i++)
                    item.Stats[i] = Random.value < 0.7f ? Mathf.Round(Random.Range(20f, 90f) * (1f + rarity * 0.7f)) : 0f;

                // The slot decides the main stat.
                item.Stats[Kinds[kind].Slot % StatNames.Length] += Mathf.Round(120f * (1f + rarity));
            }
            else
            {
                item.Count = Random.Range(1, 100);
            }

            return item;
        }

        static float StatOf(Item item, int stat)
        {
            return item == null ? 0f : Mathf.Round(item.Stats[stat] * (1f + item.Level * 0.06f));
        }

        static int PriceOf(Item item)
        {
            return (60 + item.ItemLevel * (1 + item.Rarity * item.Rarity) + item.Level * 240) * item.Count;
        }

        Slot FirstOfRarity(int rarity)
        {
            foreach (var slot in _slots)
                if (slot.Item != null && slot.Item.Rarity == rarity && slot.Item.IsGear && !slot.Item.Equipped)
                    return slot;

            return _slots[0];
        }

        // Writes what a slot shows of its item (or nothing).
        void Refresh(Slot slot)
        {
            var item = slot.Item;
            var has = item != null;
            var color = has ? Pal.Rarity[item.Rarity] : Pal.Faint;
            slot.Element.Box = Sty.Fill(has ? Color.Lerp(Pal.Ink, color, 0.2f) : Ui.Hex(0x0C0814, 0.55f)).Radius(12f)
                .Border(1.5f, has ? new Color(color.r, color.g, color.b, 0.65f) : Ui.Hex(0xFFFFFF, 0.06f));
            slot.Icon.Sprite = has ? _icons[(int)Kinds[item.Kind].Icon] : null;
            slot.Icon.Color = Color.Lerp(color, Color.white, 0.25f);
            slot.Corner.Text = has && item.Level > 0 ? $"<b>+{item.Level}</b>" : "";
            slot.Count.Text = has && item.Count > 1 ? $"<b>{item.Count}</b>" : "";

            // Parts that only some items have are collapsed, not removed: no change of structure.
            slot.Badge.Scale = has && item.Equipped ? Vector2.one : Vector2.zero;
            slot.Wear.Scale = has && item.IsGear ? Vector2.one : Vector2.zero;
            if (has && item.IsGear)
            {
                var layout = slot.WearFill.Layout;
                layout.width = Length.Percent(Mathf.Round(item.Durability * 100f));
                slot.WearFill.Layout = layout;
                slot.WearFill.BackgroundColor = item.Durability < 0.3f ? Pal.Red : Pal.Green;
            }

            // The holographic frames are in a layer of their own after the slots, so that they are consecutive in
            // the draw order: one draw call for all of them, instead of one for each.
            var legendary = has && item.Rarity == 4;
            if (legendary && slot.Frame == null && _frames != null)
            {
                slot.Frame = HoloFrame(_frames, 12f);
                slot.Frame.Layout = Lay.Col.Abs(0f, 0f, Lay.Auto, Lay.Auto).Size(SlotSize, SlotSize);
            }

            if (slot.Frame != null && !legendary) slot.Frame.Scale = Vector2.zero;

            if (legendary != _legendary.Contains(slot))
            {
                if (legendary)
                    _legendary.Add(slot);
                else
                    _legendary.Remove(slot);
            }
        }

        void Pop(Slot slot)
        {
            if (slot.Pop <= 0f) _popping.Add(slot);

            slot.Pop = 1f;
        }

        #endregion

        #region Actions

        void Select(Slot slot)
        {
            if (slot == null || slot.Item == null) return;

            _selected = slot;
            _detailFade = 0f;
            Pop(slot);
            ShowDetail();
        }

        void ShowDetail()
        {
            var item = _selected.Item;
            var kind = Kinds[item.Kind];
            var color = Pal.Rarity[item.Rarity];
            _detailTile.Box = Sty.Fill(Color.Lerp(Pal.Ink, color, 0.24f)).Radius(22f)
                .Border(2f, new Color(color.r, color.g, color.b, 0.8f))
                .Shadow(new Color(color.r, color.g, color.b, 0.45f), 16f);
            _detailIcon.Sprite = _icons[(int)kind.Icon];
            _detailIcon.Color = Color.Lerp(color, Color.white, 0.25f);
            _detailFrame.Scale = item.Rarity >= 3 ? Vector2.one : Vector2.zero;
            _detailName.Text = "<b>" + item.Name + "</b>";
            _detailName.Color = color;
            _detailType.Text = $"{RarityNames[item.Rarity]} {kind.Category}" + (item.Count > 1 ? $"  ·  x{item.Count}" : "");
            _detailLevel.Text = $"<b>iLv {item.ItemLevel}</b>";
            _detailFlavor.Text = "<i>" + Flavors[item.Flavor] + "</i>";
            _detailPrice.Text = $"<b>{PriceOf(item):N0}</b>";
            _detailEnhance.Text = item.IsGear ? $"<b>Enhancement +{item.Level}</b>" : "<b>Not enhanceable</b>";
            for (var i = 0; i < _pips.Count; i++)
                _pips[i].BackgroundColor = item.IsGear && i < item.Level ? color : Ui.Hex(0xFFFFFF, 0.1f);

            // The stats, compared with what is equipped in the same slot.
            var equipped = item.IsGear ? _gear[kind.Slot].Item : null;
            for (var i = 0; i < _detailStats.Count; i++)
            {
                var row = _detailStats[i];
                var value = item.IsGear ? StatOf(item, i) : 0f;
                var change = value - (equipped == item ? value : StatOf(equipped, i));
                row.Value.Text = item.IsGear ? $"<b>{value:N0}</b>" : "-";
                row.Value.Color = value > 0f ? Pal.Text : Pal.Faint;
                row.Change.Text = !item.IsGear || change == 0f ? "" : $"<b>{(change > 0f ? "+" : "")}{change:N0}</b>";
                row.Change.Color = change > 0f ? Pal.Green : Pal.Red;
            }

            _detailNotes.Text = item.IsGear
                ? $"Requires level {60 + item.Rarity * 6}\nDurability {Mathf.RoundToInt(item.Durability * 120f)} / 120\n" +
                  (item.Rarity >= 3 ? "<color=#F5C451>Soulbound</color>" : "Tradable")
                : $"Stack of {item.Count}\nMaximum stack 99\nTradable";
            for (var i = 0; i < _sockets.Count; i++)
            {
                var filled = item.IsGear && i < item.Rarity;
                var empty = item.IsGear && i < item.Rarity + 1;
                var gem = Pal.Rarity[(item.Kind + i) % Pal.Rarity.Length];
                _sockets[i].Box = Sty.Fill(filled ? Color.Lerp(Pal.Ink, gem, 0.35f) : Ui.Hex(0x0C0814, 0.8f)).Radius(21f)
                    .Border(1.5f, filled ? gem : Ui.Hex(0xFFFFFF, empty ? 0.25f : 0.07f));
                _socketIcons[i].Color = filled ? gem : Color.clear;
            }

            _equipLabel.Text = item.Equipped ? "<b>Equipped</b>" : item.IsGear ? "<b>Equip</b>" : "<b>Use</b>";
            _equipButton.Opacity = item.Equipped ? 0.45f : 1f;
            _enhanceButton.Opacity = item.IsGear && item.Level < 15 ? 1f : 0.45f;
        }

        void Equip()
        {
            var item = _selected?.Item;
            if (item == null || !item.IsGear || item.Equipped) return;

            var gear = _gear[Kinds[item.Kind].Slot];
            if (gear.Item != null)
            {
                gear.Item.Equipped = false;
                foreach (var slot in _slots)
                    if (slot.Item == gear.Item)
                        Refresh(slot);
            }

            item.Equipped = true;
            gear.Item = item;
            gear.Pop = 1f;
            RefreshGear(gear);
            Refresh(_selected);
            Pop(_selected);
            ShowDetail();
            Toast("EQUIPPED", Pal.Green);
        }

        void Enhance()
        {
            var item = _selected?.Item;
            if (item == null || !item.IsGear || item.Level >= 15) return;

            var cost = 400 + item.Level * 350;
            _goldTarget = Mathf.Max(_goldTarget - cost, 0f);
            item.Level++;
            Refresh(_selected);
            Pop(_selected);
            ShowDetail();
            _tilePop = 1f;
            Toast($"+{item.Level}  SUCCESS!", Pal.Gold);

            // A burst from the item: particles in the draw order of the tile.
            var color = Pal.Rarity[item.Rarity];
            for (var i = 0; i < 46; i++)
            {
                var direction = Random.insideUnitCircle.normalized;
                _burst.Emit(new ParticleSystem.EmitParams
                {
                    position = direction * Random.Range(10f, 50f),
                    velocity = direction * Random.Range(90f, 420f),
                    startSize = Random.Range(7f, 20f),
                    startLifetime = Random.Range(0.4f, 1f),
                    startColor = Color.Lerp(color, Color.white, Random.Range(0.1f, 0.7f))
                }, 1);
            }
        }

        void Sell()
        {
            var item = _selected?.Item;
            if (item == null || item.Equipped) return;

            _goldTarget += PriceOf(item);
            _selected.Item = null;
            Refresh(_selected);
            Toast($"+{PriceOf(item):N0} GOLD", Pal.Gold);
            UpdateCapacity();

            // The next item in the grid takes the selection.
            var index = _slots.IndexOf(_selected);
            for (var i = 1; i < _slots.Count; i++)
            {
                var next = _slots[(index + i) % _slots.Count];
                if (next.Item == null || !next.Element.gameObject.activeSelf) continue;

                Select(next);
                break;
            }
        }

        void Toast(string text, Color color)
        {
            _toast.Text = "<b>" + text + "</b>";
            _toast.Color = color;
            _toastTime = 0f;
        }

        void SetCategory(int category)
        {
            _category = category;
            for (var i = 0; i < _tabs.Count; i++)
            {
                var active = i == category;
                _tabs[i].Box = Sty.Fill(active ? Ui.Hex(0xF5C451, 0.18f) : Ui.Hex(0xFFFFFF, 0.04f)).Radius(15f)
                    .Border(1f, active ? Ui.Hex(0xF5C451, 0.7f) : Ui.Hex(0xFFFFFF, 0.08f));
                _tabLabels[i].Color = active ? Pal.Gold : Pal.Dim;
            }

            // Slots of other categories leave the layout: the grid closes up.
            var name = category == 0 ? null : CategoryNames[category];
            foreach (var slot in _slots)
            {
                var show = name == null || (slot.Item != null && Matches(Kinds[slot.Item.Kind].Category, name));
                if (slot.Element.gameObject.activeSelf != show) slot.Element.gameObject.SetActive(show);
            }

            _scroll.ScrollPosition = Vector2.zero;
        }

        static readonly string[] CategoryNames = { "All", "Weapons", "Armor", "Accessories", "Consumables", "Materials" };

        static bool Matches(string category, string tab)
        {
            return tab switch
            {
                "Weapons" => category is "Weapon" or "Off-hand",
                "Armor" => category == "Armor",
                "Accessories" => category == "Accessory",
                "Consumables" => category == "Consumable",
                _ => category == "Material"
            };
        }

        void UpdateCapacity()
        {
            var used = 0;
            foreach (var slot in _slots)
                if (slot.Item != null)
                    used++;

            _capacity.Text = $"<b>{used}</b> / {_slots.Count} slots";
        }

        #endregion

        #region Frame updates

        // Left alone, the screen selects items, enhances and equips them, and scrolls.
        void UpdateDemo(float dt)
        {
            if (!autoDemo || _idle < 8f) return;

            var range = _scroll.ScrollRange.y;
            if (range > 0f && _time > 3f)
            {
                var position = _scroll.ScrollPosition;
                position.y += _scrollDirection * dt * 55f;
                if (position.y >= range || position.y <= 0f) _scrollDirection = -_scrollDirection;

                position.y = Mathf.Clamp(position.y, 0f, range);
                _scroll.ScrollPosition = position;
            }

            _demoTimer -= dt;
            if (_demoTimer > 0f) return;

            _demoStep++;
            var item = _selected?.Item;
            if (_demoStep % 3 == 1 && item != null && item.IsGear && item.Level < 15)
            {
                Enhance();
                _demoTimer = 1.1f;
            }
            else if (_demoStep % 3 == 2 && item != null && item.IsGear && !item.Equipped && item.Rarity >= 2)
            {
                Equip();
                _demoTimer = 1.1f;
            }
            else
            {
                SelectVisible();
                _demoTimer = 1.3f;
            }
        }

        void SelectVisible()
        {
            // Among the slots inside the viewport, gear of the better rarities first.
            var top = _scroll.ScrollPosition.y;
            var bottom = top + _scroll.Viewport.LayoutRect.height;
            Slot best = null;
            var bestScore = -1f;
            foreach (var slot in _slots)
            {
                if (slot.Item == null || slot == _selected || !slot.Element.gameObject.activeSelf) continue;

                var rect = slot.Element.LayoutRect;
                if (rect.yMin < top || rect.yMax > bottom) continue;

                var score = Random.value + (slot.Item.IsGear ? 0.4f : 0f) + slot.Item.Rarity * 0.18f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = slot;
                }
            }

            Select(best);
        }

        void UpdateSelection(float dt)
        {
            if (_selected == null) return;

            // The frames follow their slots by their transforms.
            foreach (var slot in _legendary)
            {
                if (slot.Frame == null) continue;

                var shown = slot.Element.gameObject.activeSelf;
                var position = slot.Element.LayoutRect.position;
                if (slot.Frame.Translate != position) slot.Frame.Translate = position;
                if (slot.Frame.Scale.x > 0f != shown) slot.Frame.Scale = shown ? Vector2.one : Vector2.zero;
            }

            // The ring follows the selected slot by its transform.
            var rect = _selected.Element.LayoutRect;
            _selection.Translate = Vector2.Lerp(_selection.Translate, rect.position, 1f - Mathf.Exp(-dt * 22f));
            var pulse = 1f + 0.035f * Mathf.Sin(_time * 6f);
            _selection.Scale = _selected.Element.gameObject.activeSelf ? new Vector2(pulse, pulse) : Vector2.zero;
        }

        void UpdateAnimations(float dt)
        {
            for (var i = _popping.Count - 1; i >= 0; i--)
            {
                var slot = _popping[i];
                slot.Pop = Mathf.Max(slot.Pop - dt * 4.5f, 0f);
                var scale = 1f + 0.2f * slot.Pop * slot.Pop;
                slot.Element.Scale = new Vector2(scale, scale);
                if (slot.Pop <= 0f) _popping.RemoveAt(i);
            }

            foreach (var gear in _gear)
            {
                if (gear.Pop <= 0f) continue;

                gear.Pop = Mathf.Max(gear.Pop - dt * 3.5f, 0f);
                var scale = 1f + 0.3f * gear.Pop * gear.Pop;
                gear.Element.Scale = new Vector2(scale, scale);
            }

            if (_detailFade < 1f)
            {
                _detailFade = Mathf.Min(_detailFade + dt * 5f, 1f);
                _detail.Opacity = 0.25f + 0.75f * _detailFade;
                _detail.Translate = new Vector2((1f - _detailFade) * 14f, 0f);
            }

            _tilePop = Mathf.Max(_tilePop - dt * 3f, 0f);
            var tile = 1f + 0.14f * _tilePop * _tilePop + 0.012f * Mathf.Sin(_time * 2.2f);
            _detailTile.Scale = new Vector2(tile, tile);
            _detailIcon.Rotation = Mathf.Sin(_time * 1.3f) * 4f;

            if (_toastTime < 1f)
            {
                _toastTime = Mathf.Min(_toastTime + dt * 0.9f, 1f);
                _toast.Opacity = 1f - Mathf.InverseLerp(0.6f, 1f, _toastTime);
                _toast.Translate = new Vector2(0f, -70f * (1f - Mathf.Exp(-_toastTime * 6f)));
                var pop = 1f + 0.5f * Mathf.Exp(-_toastTime * 18f);
                _toast.Scale = new Vector2(pop, pop);
            }

            // The rune rings turn at their own speeds.
            for (var i = 0; i < _rings.Count; i++) _rings[i].Rotation = _time * (i % 2 == 0 ? 18f : -27f) * (1f + i * 0.3f);

            if (Mathf.Abs(_goldShown - _goldTarget) > 0.5f)
            {
                _goldShown = Mathf.Lerp(_goldShown, _goldTarget, 1f - Mathf.Exp(-dt * 6f));
                _gold.Text = $"<b>{Mathf.RoundToInt(_goldShown):N0}</b>";
            }
        }

        void UpdateSparkles(float dt)
        {
            // Legendary items sparkle: one particle system for the whole grid, clipped with it.
            if (_legendary.Count > 0)
            {
                var size = _content.LayoutRect.size;
                var count = Mathf.Min(Mathf.RoundToInt(dt * 60f * 0.7f + Random.value), 4);
                for (var i = 0; i < count; i++)
                {
                    var slot = _legendary[Random.Range(0, _legendary.Count)];
                    if (!slot.Element.gameObject.activeSelf) continue;

                    var rect = slot.Element.LayoutRect;
                    var at = new Vector2(Random.Range(rect.xMin, rect.xMax), Random.Range(rect.yMin, rect.yMax));
                    _gridSparkles.Emit(new ParticleSystem.EmitParams
                    {
                        position = new Vector3(at.x - size.x * 0.5f, size.y * 0.5f - at.y, 0f),
                        velocity = new Vector3(Random.Range(-10f, 10f), Random.Range(14f, 50f), 0f),
                        startSize = Random.Range(5f, 13f),
                        startLifetime = Random.Range(0.5f, 1.1f),
                        startColor = Color.Lerp(Pal.Gold, Color.white, Random.value)
                    }, 1);
                }
            }

            // The selected item sparkles by its rarity.
            var rarity = _selected?.Item?.Rarity ?? 0;
            if (rarity >= 2 && Random.value < dt * (rarity - 1) * 9f)
            {
                var direction = Random.insideUnitCircle.normalized;
                _burst.Emit(new ParticleSystem.EmitParams
                {
                    position = direction * Random.Range(40f, 80f),
                    velocity = direction * Random.Range(8f, 40f) + Vector2.up * 20f,
                    startSize = Random.Range(5f, 12f),
                    startLifetime = Random.Range(0.6f, 1.3f),
                    startColor = Color.Lerp(Pal.Rarity[rarity], Color.white, Random.Range(0.2f, 0.8f))
                }, 1);
            }
        }

        void UpdateCharacter(bool instant)
        {
            var power = 0f;
            for (var i = 0; i < _statRows.Count; i++)
            {
                var total = 180f + i * 35f;
                foreach (var gear in _gear) total += StatOf(gear.Item, i);

                power += total * (1.4f + i * 0.2f);
                var row = _statRows[i];
                row.Target = total;
                var shown = instant ? total : Mathf.Lerp(row.Shown, total, 1f - Mathf.Exp(-Time.deltaTime * 6f));
                if (Mathf.Round(shown) != Mathf.Round(row.Shown)) row.Value.Text = $"<b>{Mathf.Round(shown):N0}</b>";

                row.Shown = shown;
                row.Bar.Value = shown / 3200f;
                row.Bar.Tick(Time.deltaTime);
            }

            _powerValue = instant ? power : Mathf.Lerp(_powerValue, power, 1f - Mathf.Exp(-Time.deltaTime * 5f));
            var rounded = Mathf.RoundToInt(_powerValue);
            if (rounded != _powerShown)
            {
                _powerShown = rounded;
                _power.Text = $"<b>{rounded:N0}</b>";
            }
        }

        void RefreshGear(Gear gear)
        {
            var item = gear.Item;
            var color = item != null ? Pal.Rarity[item.Rarity] : Pal.Faint;
            gear.Element.Box = Sty.Fill(item != null ? Color.Lerp(Pal.Ink, color, 0.22f) : Ui.Hex(0x0C0814, 0.7f))
                .Radius(14f).Border(1.5f, new Color(color.r, color.g, color.b, item != null ? 0.75f : 0.3f));
            gear.Icon.Color = item != null ? Color.Lerp(color, Color.white, 0.25f) : Ui.Hex(0xFFFFFF, 0.18f);
            gear.Frame.Scale = item != null && item.Rarity == 4 ? Vector2.one : Vector2.zero;
        }

        #endregion
    }
}
