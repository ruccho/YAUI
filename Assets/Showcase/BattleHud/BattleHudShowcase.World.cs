using System.Collections.Generic;
using UnityEngine;

namespace Yaui.Showcase
{
    // The 3D scene behind the HUD, and the world space panels in it: a nameplate per unit.
    public sealed partial class BattleHudShowcase
    {
        sealed class Enemy
        {
            public Transform Transform;
            public Material Material;
            public string Name;
            public bool Boss;
            public float Health;
            public float MaxHealth;
            public float Height;
            public float Scale;
            public float Flash;
            public float Respawn;
            public float Speed;
            public Vector3 Target;
            public Transform Plate;
            public Bar PlateHp;
        }

        static readonly string[] EnemyNames =
            { "Ash Wraith", "Cinder Imp", "Gloom Stalker", "Ember Hound", "Rift Acolyte", "Obsidian Golem" };

        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly Color Fog = Ui.Hex(0x070A14);

        Camera _camera;
        Transform _world;
        Shader _backdrop;
        readonly List<Enemy> _enemies = new();
        readonly List<Enemy> _allies = new();
        readonly List<Transform> _orbiters = new();

        void BuildWorld()
        {
            _world = new GameObject("World").transform;
            _backdrop = Find(backdropShader, "Yaui/Showcase/Backdrop");

            _camera = worldCamera != null ? worldCamera : Camera.main;
            if (_camera == null)
            {
                _camera = new GameObject("Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            }

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Fog;
            _camera.fieldOfView = 46f;

            var ground = Body(PrimitiveType.Plane, "Ground", Ui.Hex(0x0A1024), Ui.Hex(0x3B6BFF), out _);
            ground.localScale = new Vector3(40f, 1f, 40f);
            ground.GetComponent<Renderer>().sharedMaterial.SetFloat("_Grid", 1f);

            Body(PrimitiveType.Capsule, "Player", Ui.Hex(0x12406A), Pal.Cyan, out _).position = Vector3.up;

            // The boss, with shards orbiting it.
            var boss = AddEnemy("Vorathiel", PrimitiveType.Capsule, 3.2f, Ui.Hex(0x3A0E2E), Pal.Red, 5200000f);
            boss.Boss = true;
            boss.Transform.position = new Vector3(0f, boss.Scale, 13f);
            boss.Target = boss.Transform.position;
            for (var i = 0; i < 8; i++)
            {
                var shard = Body(PrimitiveType.Cube, "Shard", Ui.Hex(0x4A1040), Pal.Purple, out _);
                shard.localScale = Vector3.one * Random.Range(0.5f, 1.1f);
                _orbiters.Add(shard);
            }

            for (var i = 0; i < 14; i++)
            {
                var name = EnemyNames[i % EnemyNames.Length];
                var tint = Color.Lerp(Pal.Red, Pal.Orange, Random.value);
                var enemy = AddEnemy(name, i % 3 == 2 ? PrimitiveType.Cube : PrimitiveType.Capsule,
                    Random.Range(0.8f, 1.25f), Color.Lerp(Ui.Hex(0x1A0A12), tint, 0.25f), tint,
                    Random.Range(160000f, 420000f));
                enemy.Speed = Random.Range(0.6f, 2f);
                enemy.Transform.position = RandomSpot(enemy.Scale);
                enemy.Target = RandomSpot(enemy.Scale);
                enemy.Health = enemy.MaxHealth * Random.Range(0.3f, 1f);
                AddPlate(enemy, $"{Ui.Tag(Pal.Gold)}Lv.{Random.Range(84, 90)}</color> <b>{name}</b>", Pal.Red, 0.0105f);
            }

            for (var i = 0; i < 6; i++)
            {
                var color = Pal.Classes[i % Pal.Classes.Length];
                var ally = new Enemy { Scale = 0.9f, Height = 1.8f, Speed = Random.Range(0.4f, 1f) };
                ally.Transform = Body(PrimitiveType.Capsule, MemberNames[i + 1], Color.Lerp(Pal.Ink, color, 0.4f),
                    color, out ally.Material);
                ally.Transform.localScale = Vector3.one * ally.Scale;
                ally.Transform.position = AllySpot();
                ally.Target = AllySpot();
                AddPlate(ally, $"<b>{MemberNames[i + 1]}</b>", Pal.Green, 0.0075f);
                _allies.Add(ally);
            }

            UpdateWorld(0f);
        }

        Transform Body(PrimitiveType type, string name, Color color, Color rim, out Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(_world, false);
            if (go.TryGetComponent<Collider>(out var collider)) Destroy(collider);

            material = new Material(_backdrop);
            material.SetColor("_Color", color);
            material.SetColor("_RimColor", rim);
            material.SetColor("_FogColor", Fog);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        Enemy AddEnemy(string name, PrimitiveType type, float scale, Color color, Color rim, float health)
        {
            var enemy = new Enemy
            {
                Name = name,
                Scale = scale,
                Height = scale * 2f,
                Health = health,
                MaxHealth = health
            };
            enemy.Transform = Body(type, name, color, rim, out enemy.Material);
            enemy.Transform.localScale = Vector3.one * scale;
            _enemies.Add(enemy);
            return enemy;
        }

        // A world space panel: sorted and depth tested with the scene, facing the camera.
        void AddPlate(Enemy unit, string label, Color color, float scale)
        {
            var go = new GameObject("Nameplate");
            go.transform.SetParent(_world, false);
            var panel = go.AddComponent<YauiPanel>();
            _panels.Add(panel);
            panel.ReferenceResolution = new Vector2(280f, 64f);
            panel.WorldScale = scale;
            panel.RenderMode = PanelRenderMode.World;

            var root = go.GetComponent<YauiElement>();
            root.RaycastTarget = false;
            root.Layout = Lay.Col.Items(FlexAlign.Center).Justify(FlexJustify.FlexEnd).Gap(5f);
            Ui.Text(root, "Name", label, 23f, Color.white).Shadowed(4f);
            unit.PlateHp = Bar.Create(root, "Health", Lay.Row.Size(190f, 15f).Fixed(), 7f, color, null, true);
            unit.Plate = go.transform;
        }

        static Vector3 RandomSpot(float height)
        {
            var angle = Random.Range(-70f, 70f) * Mathf.Deg2Rad;
            var radius = Random.Range(4.5f, 12.5f);
            return new Vector3(Mathf.Sin(angle) * radius * 1.25f, height, Mathf.Cos(angle) * radius - 1f);
        }

        static Vector3 AllySpot()
        {
            var p = Random.insideUnitCircle * 3.2f;
            return new Vector3(p.x, 0.9f, p.y - 1.5f);
        }

        void UpdateWorld(float dt)
        {
            var time = Time.time;
            var sway = Mathf.Sin(time * 0.17f) * 0.2f;
            _camera.transform.position = new Vector3(Mathf.Sin(sway) * 17f, 9.5f + Mathf.Sin(time * 0.11f) * 0.5f,
                -Mathf.Cos(sway) * 17f + 2f);
            _camera.transform.LookAt(new Vector3(0f, 1.6f, 4.5f));

            var boss = _enemies[0];
            boss.Transform.position = boss.Target + Vector3.up * Mathf.Sin(time * 1.3f) * 0.3f;
            for (var i = 0; i < _orbiters.Count; i++)
            {
                var a = time * 0.7f + i * Mathf.PI * 2f / _orbiters.Count;
                _orbiters[i].position = boss.Target + new Vector3(Mathf.Cos(a) * 6f, Mathf.Sin(a * 2f + i) * 2.4f,
                    Mathf.Sin(a) * 6f);
                _orbiters[i].Rotate(40f * dt, 65f * dt, 20f * dt);
            }

            foreach (var e in _enemies)
            {
                if (e.Flash > 0f)
                {
                    e.Flash = Mathf.Max(e.Flash - dt * 6f, 0f);
                    e.Material.SetFloat(FlashId, e.Flash * 0.8f);
                }

                if (e.Boss) continue;

                if (e.Respawn > 0f)
                {
                    e.Respawn -= dt;
                    if (e.Respawn <= 0f)
                    {
                        e.Health = e.MaxHealth;
                        e.Transform.position = RandomSpot(e.Scale);
                        e.Plate.gameObject.SetActive(true);
                    }
                    else if (e.Plate.gameObject.activeSelf && e.Respawn < 1f)
                    {
                        e.Plate.gameObject.SetActive(false);
                    }

                    // Sinks when defeated, rises when it returns.
                    var shown = e.Respawn > 0f ? Mathf.Clamp01(e.Respawn - 1f) : 1f;
                    e.Transform.localScale = Vector3.one * e.Scale * Mathf.Max(shown, 0.001f);
                    continue;
                }

                e.Transform.localScale = Vector3.MoveTowards(e.Transform.localScale, Vector3.one * e.Scale, dt * 3f);
                Wander(e, dt, RandomSpot(e.Scale));
            }

            foreach (var a in _allies) Wander(a, dt, AllySpot());
        }

        static void Wander(Enemy unit, float dt, Vector3 next)
        {
            var position = Vector3.MoveTowards(unit.Transform.position, unit.Target, unit.Speed * dt);
            unit.Transform.position = position;
            if ((position - unit.Target).sqrMagnitude < 0.01f) unit.Target = next;
        }

        void UpdateNameplates(float dt)
        {
            var rotation = _camera.transform.rotation;
            foreach (var e in _enemies)
            {
                if (e.Plate == null) continue;

                e.Plate.SetPositionAndRotation(e.Transform.position + Vector3.up * (e.Scale + 0.9f), rotation);
                e.PlateHp.Value = Mathf.Clamp01(e.Health / e.MaxHealth);
                e.PlateHp.Tick(dt);
            }

            for (var i = 0; i < _allies.Count; i++)
            {
                var a = _allies[i];
                a.Plate.SetPositionAndRotation(a.Transform.position + Vector3.up * (a.Scale + 0.9f), rotation);
                a.PlateHp.Value = _members[i + 1].Health;
                a.PlateHp.Tick(dt);
            }
        }

        void UpdateMinimap(float dt)
        {
            _radar.Rotation += dt * 120f;

            // 24 world units from the player to the rim. Only transforms change: no layout.
            const float scale = 110f / 24f;
            for (var i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                var p = e.Transform.position;
                _mapDots[i].Translate = new Vector2(p.x, -p.z) * scale;
                var pulse = e.Respawn > 0f ? 0f : 1f + 0.5f * e.Flash;
                _mapDots[i].Scale = new Vector2(pulse, pulse);
            }
        }
    }
}
