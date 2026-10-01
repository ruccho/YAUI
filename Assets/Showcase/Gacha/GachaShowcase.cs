using System.Collections.Generic;
using UnityEngine;

namespace Yaui.Showcase
{
    /// <summary>
    /// The shop of a mobile game, built in code at startup: a gacha banner carousel cut to a slanted shape by a
    /// mask, product cards, a battle pass with a scrolling reward track, and the reveal of a ten-pull with confetti.
    /// Everything can be pressed; left alone, the screen demonstrates itself.
    /// </summary>
    public sealed partial class GachaShowcase : MonoBehaviour
    {
        [SerializeField] Shader gradientShader;
        [SerializeField] Shader sunburstShader;
        [SerializeField] Shader sheenShader;
        [SerializeField] Shader holoShader;
        [SerializeField] Shader particleShader;

        /// <summary>Pulls, buys and claims by itself until the pointer is used.</summary>
        [SerializeField] bool autoDemo = true;

        const int PullCost = 160;
        const int PityLimit = 60;
        const int TierCount = 50;
        const float TierWidth = 118f;

        static class Pal
        {
            public static readonly Color Ink = Ui.Hex(0x1E1B4B);
            public static readonly Color Dim = Ui.Hex(0x6B6A8C);
            public static readonly Color Pink = Ui.Hex(0xFF5FA2);
            public static readonly Color Blue = Ui.Hex(0x3B82F6);
            public static readonly Color Sky = Ui.Hex(0x38BDF8);
            public static readonly Color Yellow = Ui.Hex(0xFFD23F);
            public static readonly Color Purple = Ui.Hex(0x8B5CF6);
            public static readonly Color Mint = Ui.Hex(0x34D399);
            public static readonly Color Orange = Ui.Hex(0xFF8A3D);
            public static readonly Color[] Party = { Pink, Sky, Yellow, Purple, Mint, Orange, Color.white };

            public static BoxStyle Card => Sty.Fill(Color.white).Radius(22f)
                .Shadow(Ui.Hex(0x1E1B4B, 0.2f), 12f, 0f, new Vector2(0f, 8f));
        }

        // Top and bottom of the face of a pulled card, by rarity: R, SR, SSR.
        static readonly Color[] RarityTop = { Ui.Hex(0x7DD3FC), Ui.Hex(0xC4B5FD), Ui.Hex(0xFFE873) };
        static readonly Color[] RarityBottom = { Ui.Hex(0x2563EB), Ui.Hex(0x7C3AED), Ui.Hex(0xFF7A1A) };
        static readonly string[] RarityNames = { "R", "SR", "SSR" };

        static readonly string[][] CardNames =
        {
            new[] { "Tin Charm", "Paper Kite", "Glass Bead", "Field Flask", "Copper Bell" },
            new[] { "Comet Ribbon", "Moonlit Lantern", "Aurora Fan", "Tidal Compass" },
            new[] { "Starlight Crown", "Sunfire Halo", "Eternal Prism", "星詠みの冠" }
        };

        struct Floater
        {
            public YauiElement Element;
            public float Phase;
            public float Amplitude;
            public float Spin;
        }

        sealed class PullCard
        {
            // The glow, the face, the content and the frame: in a grid each, moved together.
            public YauiElement[] Parts;
            public YauiElement Glow;
            public YauiElement Face;
            public YauiElement Frame;
            public YauiElement New;
            public YauiImage Icon;
            public YauiText Rarity;
            public YauiText Name;
            public int Tier;
            public bool Burst;
        }

        sealed class Product
        {
            public YauiElement Card;
            public int Gems;
            public float Pop;
        }

        sealed class PassTier
        {
            public YauiElement Free;
            public YauiElement Premium;
            public YauiElement FreeCheck;
            public YauiElement PremiumCheck;
            public YauiElement Node;
            public YauiElement Line;
            public YauiElement Claim;
            public YauiText Number;
        }

        readonly List<YauiPanel> _panels = new();
        readonly ShowcaseStats _counter = new();
        readonly List<Floater> _floaters = new();
        readonly List<PullCard> _cards = new();
        readonly List<Product> _products = new();
        readonly List<PassTier> _tiers = new();
        readonly List<YauiElement> _dots = new();
        readonly List<YauiElement> _popping = new();

        Sprite[] _icons;
        Material _gradient;
        Material _sheen;
        Material _holo;
        Material _glow;
        YauiPanel _panel;
        YauiElement _root;

        SkewElement _banner;
        YauiElement _strip;
        int _page;
        float _pageShown;
        float _pageTimer = 4.5f;
        YauiText _countdown;
        float _remaining = 2 * 86400 + 14 * 3600 + 23 * 60 + 51;
        int _countdownShown = -1;
        YauiText _pityText;
        YauiElement _pityFill;
        int _pity = 37;

        YauiText _gems;
        float _gemsShown = 3240f;
        float _gemsTarget = 3240f;

        YauiScrollView _passScroll;
        YauiElement _passFill;
        YauiText _passLevel;
        int _level = 13;
        float _experience = 0.55f;
        float _experienceShown = -1f;

        YauiElement _overlay;
        YauiText _overlayHint;
        float _pullTime = -1f;
        float _closeTime = -1f;
        int _pullCount;
        int _pulls;
        bool _manualPull;

        ParticleSystem _confetti;
        ParticleSystem _sparkles;
        YauiText _toast;
        float _toastTime = 1f;
        Vector2 _toastAt;

        YauiText _stats;
        float _time;
        float _idle = 99f;
        float _demoTimer = 3.5f;
        int _demoStep;
        float _slowTimer;
        float _statsTimer;
        float _smoothedDelta = 1f / 60f;

        void Start()
        {
            Random.InitState(20261004);
            _icons = IconFactory.CreateAll();
            _gradient = new Material(Find(gradientShader, "Yaui/Showcase/Gradient"));
            _sheen = new Material(Find(sheenShader, "Yaui/Showcase/Sheen"));
            _holo = new Material(Find(holoShader, "Yaui/Showcase/Holo"));
            _glow = new Material(Find(sunburstShader, "Yaui/Showcase/Sunburst"));
            _glow.SetColor("_ColorA", Color.white);
            _glow.SetColor("_ColorB", Ui.Hex(0xFFF2B0));
            _glow.SetColor("_RayColor", new Color(1f, 1f, 1f, 1f));
            _glow.SetFloat("_Rays", 12f);
            _glow.SetFloat("_Speed", 0.5f);
            _glow.SetFloat("_Fade", 1f);
            _glow.SetFloat("_Additive", 1f);
            Fx.EnsureEventSystem();

            BuildShop();
            for (var i = 0; i < _tiers.Count; i++) RefreshTier(i);
        }

        static Shader Find(Shader shader, string name)
        {
            return shader != null ? shader : Shader.Find(name);
        }

        void Update()
        {
            var dt = Time.deltaTime;
            _time += dt;
            _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);
            _idle = Fx.PointerInUse() ? 0f : _idle + dt;

            UpdateDemo(dt);
            UpdateFloaters();
            UpdateBanner(dt);
            UpdatePass(dt);
            UpdatePull(dt);
            UpdateFeedback(dt);

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
                _stats.Text = $"<b>{_counter.Primitives:N0}</b> quads   <b>{_counter.Elements:N0}</b> elements   " +
                              $"<b>{_counter.DrawCalls}</b> draw calls   <b>{1f / _smoothedDelta:0}</b> fps";
            }
        }

        // Left alone, the shop pulls, buys and claims.
        void UpdateDemo(float dt)
        {
            if (!autoDemo || _idle < 6f || _pullTime >= 0f) return;

            _demoTimer -= dt;
            if (_demoTimer > 0f) return;

            _demoStep++;
            if (_demoStep % 3 == 1)
            {
                Pull(10, false);
                _demoTimer = 3f;
            }
            else if (_demoStep % 3 == 2)
            {
                Buy(_products[Random.Range(0, _products.Count)]);
                _demoTimer = 2.2f;
            }
            else
            {
                _experience = 1f;
                _demoTimer = 2.2f;
            }
        }

        #region Banner

        void Float(YauiElement element, float amplitude, float spin = 0f)
        {
            _floaters.Add(new Floater
            {
                Element = element, Phase = Random.value * 6.28f, Amplitude = amplitude, Spin = spin
            });
        }

        // The art of the banners bobs and turns: transforms only.
        void UpdateFloaters()
        {
            foreach (var f in _floaters)
            {
                var t = _time * 1.4f + f.Phase;
                f.Element.Translate = new Vector2(Mathf.Sin(t * 0.7f) * f.Amplitude * 0.4f, Mathf.Sin(t) * f.Amplitude);
                if (f.Spin != 0f) f.Element.Rotation = _time * f.Spin + f.Phase * 57f;
            }
        }

        void ShowPage(int page)
        {
            _page = (page + _dots.Count) % _dots.Count;
            _pageTimer = 5f;
        }

        void UpdateBanner(float dt)
        {
            _pageTimer -= dt;
            if (_pageTimer <= 0f) ShowPage(_page + 1);

            // The pages slide inside the mask.
            _pageShown = Mathf.Lerp(_pageShown, _page, 1f - Mathf.Exp(-dt * 7f));
            _strip.Translate = new Vector2(-_pageShown * _banner.LayoutRect.width, 0f);
            for (var i = 0; i < _dots.Count; i++)
            {
                var active = Mathf.Clamp01(1f - Mathf.Abs(_pageShown - i));
                _dots[i].Scale = new Vector2(1f + active * 1.6f, 1f);
                _dots[i].Opacity = 0.45f + 0.55f * active;
            }

            _remaining = Mathf.Max(_remaining - dt, 0f);
            var seconds = (int)_remaining;
            if (seconds != _countdownShown)
            {
                _countdownShown = seconds;
                _countdown.Text = $"<b>{seconds / 86400}d {seconds / 3600 % 24:00}:{seconds / 60 % 60:00}:{seconds % 60:00}</b>";
            }

            if (Mathf.Abs(_gemsShown - _gemsTarget) > 0.5f)
            {
                _gemsShown = Mathf.Lerp(_gemsShown, _gemsTarget, 1f - Mathf.Exp(-dt * 7f));
                _gems.Text = $"<b>{Mathf.RoundToInt(_gemsShown):N0}</b>";
            }
        }

        void UpdatePity()
        {
            _pityText.Text = $"Guaranteed <b>SSR</b> in <b>{PityLimit - _pity}</b> pulls";
            var layout = _pityFill.Layout;
            layout.width = Length.Percent(Mathf.Round(100f * _pity / PityLimit));
            _pityFill.Layout = layout;
        }

        #endregion

        #region Pull

        void Pull(int count, bool manual)
        {
            if (_pullTime >= 0f) return;

            // The demo never runs out of gems.
            var cost = count * PullCost;
            if (_gemsTarget < cost) _gemsTarget += 6000f;

            _gemsTarget -= cost;
            _pulls++;
            _pullCount = count;
            _manualPull = manual;
            var best = 0;
            for (var i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                foreach (var part in card.Parts)
                    if (part.gameObject.activeSelf != i < count)
                        part.gameObject.SetActive(i < count);

                if (i >= count) continue;

                _pity++;
                var roll = Random.value;
                var tier = _pity >= PityLimit || roll < 0.04f ? 2 : roll < 0.3f ? 1 : 0;
                // Ten pulls have at least an SR, and every other one of the demo shows an SSR.
                if (i == count - 1 && count == 10 && best == 0) tier = 1;
                if (i == 6 && count == 10 && best < 2 && _pulls % 2 == 1) tier = 2;

                if (tier == 2) _pity = 0;

                best = Mathf.Max(best, tier);
                Deal(card, tier);
            }

            UpdatePity();
            _overlayHint.Text = "";
            _overlay.Scale = Vector2.one;
            _pullTime = 0f;
            _closeTime = -1f;
        }

        void Deal(PullCard card, int tier)
        {
            card.Tier = tier;
            card.Burst = false;
            card.Face.Box = new BoxStyle
            {
                backgroundColor = RarityTop[tier],
                borderColor = RarityBottom[tier],
                cornerRadius = new Vector4(20f, 20f, 20f, 20f)
            };
            card.Icon.Sprite = _icons[Random.Range(0, 16)];
            card.Rarity.Text = "<b>" + RarityNames[tier] + "</b>";
            var names = CardNames[tier];
            card.Name.Text = "<b>" + names[Random.Range(0, names.Length)] + "</b>";
            card.Frame.Scale = tier == 2 ? Vector2.one : Vector2.zero;
            card.New.Scale = tier > 0 && Random.value < 0.6f ? Vector2.one : Vector2.zero;
            card.Glow.Scale = Vector2.zero;
            foreach (var part in card.Parts) part.Scale = Vector2.zero;
        }

        void ClosePull()
        {
            if (_pullTime >= RevealEnd && _closeTime < 0f) _closeTime = 0f;
        }

        float RevealEnd => 0.4f + _pullCount * 0.12f + 0.45f;

        void UpdatePull(float dt)
        {
            if (_pullTime < 0f) return;

            _pullTime += dt;
            var alpha = Mathf.Clamp01(_pullTime / 0.25f);
            if (_closeTime >= 0f)
            {
                _closeTime += dt;
                alpha = 1f - _closeTime / 0.3f;
                if (alpha <= 0f)
                {
                    // Collapsed: the overlay costs no pixels while it is hidden.
                    _pullTime = -1f;
                    _overlay.Scale = Vector2.zero;
                    return;
                }
            }
            else if (_pullTime > RevealEnd)
            {
                if (_overlayHint.Text.Length == 0) _overlayHint.Text = "Tap to continue";
                if (!_manualPull && _pullTime > RevealEnd + 2.8f) _closeTime = 0f;
            }

            _overlay.Opacity = alpha;
            for (var i = 0; i < _pullCount; i++)
            {
                var card = _cards[i];
                var p = Mathf.Clamp01((_pullTime - 0.4f - i * 0.12f) / 0.45f);
                if (p <= 0f) continue;

                // Each card flips in: transforms and opacity only.
                var back = 1f + 2.2f * Mathf.Pow(p - 1f, 3f) + 1.2f * Mathf.Pow(p - 1f, 2f);
                var bob = p >= 1f ? Mathf.Sin(_time * 2.2f + i) * 5f : 0f;
                foreach (var part in card.Parts)
                {
                    part.Scale = new Vector2(Mathf.Min(p * 2.4f, 1f) * back, back);
                    part.Rotation = (1f - p) * -22f;
                    part.Translate = new Vector2(0f, (1f - p) * 110f + bob);
                    part.Opacity = Mathf.Min(p * 3f, 1f);
                }

                if (card.Tier < 2) continue;

                var glow = back * (1f + 0.06f * Mathf.Sin(_time * 5f));
                card.Glow.Scale = new Vector2(glow, glow);
                if (!card.Burst && p > 0.4f)
                {
                    card.Burst = true;
                    if (TryCenter(card.Face, out var center)) Confetti(center, 90, 1f);
                }
            }
        }

        #endregion

        #region Feedback

        bool TryCenter(YauiElement element, out Vector2 canvas)
        {
            canvas = default;
            return element.LocalToScreen(element.LayoutRect.size * 0.5f, out var screen) &&
                   _panel.TryScreenToCanvas(screen, out canvas);
        }

        // The particle systems simulate around the center of the screen, with Y up.
        Vector3 ToParticles(Vector2 canvas)
        {
            var size = _panel.CanvasSize;
            return new Vector3(canvas.x - size.x * 0.5f, size.y * 0.5f - canvas.y, 0f);
        }

        void Confetti(Vector2 canvas, int count, float power)
        {
            var position = ToParticles(canvas);
            for (var i = 0; i < count; i++)
            {
                var angle = Random.Range(20f, 160f) * Mathf.Deg2Rad;
                var speed = Random.Range(280f, 980f) * power;
                _confetti.Emit(new ParticleSystem.EmitParams
                {
                    position = position + (Vector3)Random.insideUnitCircle * 30f,
                    velocity = new Vector3(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed, 0f),
                    startSize3D = new Vector3(Random.Range(10f, 20f), Random.Range(6f, 11f), 1f),
                    rotation = Random.Range(0f, 360f),
                    angularVelocity = Random.Range(-540f, 540f),
                    startLifetime = Random.Range(1.3f, 2.6f),
                    startColor = Pal.Party[Random.Range(0, Pal.Party.Length)]
                }, 1);
            }
        }

        void Sparkle(Vector2 canvas, int count, Color color)
        {
            var position = ToParticles(canvas);
            for (var i = 0; i < count; i++)
            {
                var direction = Random.insideUnitCircle.normalized;
                _sparkles.Emit(new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = direction * Random.Range(80f, 380f),
                    startSize = Random.Range(8f, 22f),
                    startLifetime = Random.Range(0.4f, 0.9f),
                    startColor = Color.Lerp(color, Color.white, Random.Range(0.2f, 0.8f))
                }, 1);
            }
        }

        void Pop(YauiElement element)
        {
            if (!_popping.Contains(element)) _popping.Add(element);

            element.Scale = new Vector2(1.16f, 1.16f);
        }

        void Toast(Vector2 canvas, string text, Color color)
        {
            _toast.Text = "<b>" + text + "</b>";
            _toast.Color = color;
            _toastAt = canvas;
            _toastTime = 0f;
        }

        void Buy(Product product)
        {
            _gemsTarget += product.Gems;
            Pop(product.Card);
            if (!TryCenter(product.Card, out var center)) return;

            Toast(center, $"+{product.Gems:N0}", Pal.Sky);
            Confetti(center, 34, 0.7f);
        }

        void UpdateFeedback(float dt)
        {
            for (var i = _popping.Count - 1; i >= 0; i--)
            {
                var scale = Mathf.MoveTowards(_popping[i].Scale.x, 1f, dt * 0.9f);
                _popping[i].Scale = new Vector2(scale, scale);
                if (scale <= 1f) _popping.RemoveAt(i);
            }

            if (_toastTime < 1f)
            {
                _toastTime = Mathf.Min(_toastTime + dt * 0.8f, 1f);
                var rise = 110f * (1f - Mathf.Exp(-_toastTime * 5f));
                var pop = 1f + 0.6f * Mathf.Exp(-_toastTime * 16f);
                _toast.Translate = _toastAt - new Vector2(200f, 40f + rise);
                _toast.Scale = new Vector2(pop, pop);
                _toast.Opacity = 1f - Mathf.InverseLerp(0.6f, 1f, _toastTime);
            }
        }

        #endregion

        #region Pass

        void RefreshTier(int index)
        {
            var tier = _tiers[index];
            var claimed = index < _level;
            var current = index == _level;
            tier.Free.Opacity = tier.Premium.Opacity = claimed ? 0.5f : 1f;
            tier.FreeCheck.Scale = tier.PremiumCheck.Scale = claimed ? Vector2.one : Vector2.zero;
            tier.Claim.Scale = current ? Vector2.one : Vector2.zero;
            tier.Line.BackgroundColor = claimed ? Pal.Pink : Ui.Hex(0xDADCF0);
            tier.Node.Box = Sty.Fill(claimed ? Pal.Pink : current ? Pal.Yellow : Color.white).Radius(15f)
                .Border(3f, claimed ? Pal.Pink : current ? Pal.Orange : Ui.Hex(0xDADCF0));
            tier.Number.Color = claimed ? Color.white : current ? Pal.Ink : Pal.Dim;
        }

        void ClaimTier()
        {
            if (_level >= TierCount - 1) _level = 5;

            var tier = _tiers[_level];
            _level++;
            _experience = 0f;
            for (var i = 0; i < _tiers.Count; i++) RefreshTier(i);

            Pop(tier.Free);
            Pop(tier.Premium);
            if (TryCenter(tier.Node, out var center))
            {
                Sparkle(center, 30, Pal.Yellow);
                Toast(center, "CLAIMED!", Pal.Pink);
            }
        }

        void UpdatePass(float dt)
        {
            _experience += dt * 0.05f;
            if (_experience >= 1f) ClaimTier();

            var percent = Mathf.Round(_experience * 200f) * 0.5f;
            if (percent != _experienceShown)
            {
                _experienceShown = percent;
                var layout = _passFill.Layout;
                layout.width = Length.Percent(percent);
                _passFill.Layout = layout;
                _passLevel.Text = $"<b>{_level + 1}</b>";
            }

            // The claim button of the current tier beats.
            var beat = 1f + 0.07f * Mathf.Sin(_time * 7f);
            _tiers[_level].Claim.Scale = new Vector2(beat, beat);

            // The track follows the current tier: scrolling is a transform of the content.
            if (_idle < 6f) return;

            var range = _passScroll.ScrollRange.x;
            if (range <= 0f) return;

            var target = Mathf.Clamp((_level + 0.5f) * TierWidth - _passScroll.Viewport.LayoutRect.width * 0.4f, 0f, range);
            var position = _passScroll.ScrollPosition;
            position.x = Mathf.Lerp(position.x, target, 1f - Mathf.Exp(-dt * 3f));
            _passScroll.ScrollPosition = position;
        }

        #endregion
    }
}
