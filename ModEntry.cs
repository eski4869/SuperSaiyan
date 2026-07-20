using System;
using System.Collections.Generic;
using System.Reflection;
using EntityComponent;
using JumpKing;
using JumpKing.Level;
using JumpKing.Mods;
using JumpKing.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace SuperSaiyan
{
    [JumpKingMod("eski4869.SuperSaiyan")]
    public static class ModEntry
    {
        [BeforeLevelLoad]
        public static void BeforeLevelLoad()
        {
            SuperSaiyanAura.RegisterCommandTarget();
        }

        [OnLevelStart]
        public static void OnLevelStart()
        {
            SuperSaiyanAura.RegisterCommandTarget();
            SuperSaiyanAura.EnsureAdded();
        }
    }

    public sealed class SuperSaiyanAura :
        Entity,
        JumpKing.Util.IDrawable,
        JumpKing.Util.Tags.IForeground
    {
        private const float AuraDurationSeconds = 20f;
        private const float KamehamehaChargeSeconds = 1f;
        private const float KamehamehaBeamSeconds = 5f;
        private const float KamehamehaDurationSeconds =
            KamehamehaChargeSeconds + KamehamehaBeamSeconds;
        private const int KamehamehaMaximumLength = 480;
        private const int KamehamehaCollisionHeight = 40;
        private const float KamehamehaDamageSampleSeconds = 0.25f;
        private const float GenkidamaChargeSeconds = 2.5f;
        private const float GenkidamaExplosionSeconds = 1.8f;
        private const float GenkidamaSpeed = 55f;
        private const int GenkidamaRadius = 22;
        private const int GenkidamaDamageRadius = 82;
        private const int GenkidamaOffscreenExplosionDepth = 52;
        private const string CommandTarget = "super_saiyan";
        private const string ActivateCommand = "activate";
        private const string KamehamehaCommand = "kamehameha";
        private const string GenkidamaCommand = "genkidama";
        private const string DeactivateCommand = "deactivate";

        private static SuperSaiyanAura _instance;
        private static readonly FieldInfo PlayerFlipField = typeof(PlayerEntity).GetField(
            "m_flip",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        private Texture2D[] _auraFrames;
        private Texture2D _pixel;
        private KeyboardState _previousKeyboardState;
        private float _remainingSeconds;
        private float _kamehamehaSeconds;
        private float _animationSeconds;
        private int _damageSeed;
        private int _damageScreenIndex = -1;
        private Point? _lastRightDamageOrigin;
        private Point? _lastLeftDamageOrigin;
        private int _lastKamehamehaDamageSample = -1;
        private GenkidamaPhase _genkidamaPhase;
        private float _genkidamaPhaseSeconds;
        private Vector2 _genkidamaWorldPosition;
        private int _genkidamaDirection = 1;
        private int _lastHorizontalDirection = 1;
        private readonly List<DamageMark> _damageMarks = new List<DamageMark>();

        public static void RegisterCommandTarget()
        {
            BrokerCommandClient.Register(CommandTarget);
        }

        public static void EnsureAdded()
        {
            if (EntityManager.instance == null)
            {
                return;
            }

            if (_instance != null && _instance.IsAlive)
            {
                return;
            }

            _instance = new SuperSaiyanAura();
            EntityManager.instance.AddObject(_instance);
        }

        private SuperSaiyanAura()
        {
            _previousKeyboardState = Keyboard.GetState();
            BrokerCommandClient.Register(CommandTarget);
            CreateTextures();
        }

        protected override void Update(float delta)
        {
            _animationSeconds += delta;
            UpdateDamageScreen();

            KeyboardState keyboardState = Keyboard.GetState();
            TrackHorizontalDirection(keyboardState);
            ProcessBrokerCommands();

            bool shiftDown =
                keyboardState.IsKeyDown(Keys.LeftShift) ||
                keyboardState.IsKeyDown(Keys.RightShift);

            if (shiftDown && WasKeyPressed(keyboardState, Keys.S))
            {
                ActivateAura();
            }

            if (shiftDown && WasKeyPressed(keyboardState, Keys.C))
            {
                FireKamehameha();
            }

            if (shiftDown && WasKeyPressed(keyboardState, Keys.G))
            {
                StartGenkidama();
            }

            _previousKeyboardState = keyboardState;



            if (_remainingSeconds > 0f)
            {
                _remainingSeconds = Math.Max(0f, _remainingSeconds - delta);
            }

            if (_kamehamehaSeconds > 0f)
            {
                CaptureKamehamehaDamage();
                _kamehamehaSeconds = Math.Max(0f, _kamehamehaSeconds - delta);
            }

            UpdateGenkidama(delta);
        }

        public override void Draw()
        {
            if ((_remainingSeconds <= 0f &&
                 _kamehamehaSeconds <= 0f &&
                 _genkidamaPhase == GenkidamaPhase.None) ||
                _pixel == null)
            {
                return;
            }

            if (Game1.instance == null)
            {
                return;
            }

            if (_remainingSeconds <= 0f &&
                _kamehamehaSeconds <= 0f &&
                _genkidamaPhase == GenkidamaPhase.None)
            {
                return;
            }

            PlayerEntity player = EntityManager.instance.Find<PlayerEntity>();
            if (player != null)
            {
                Rectangle hitbox = Camera.TransformRect(player.m_body.GetHitbox());

                if (_remainingSeconds > 0f)
                {
                    DrawEnergyAura(hitbox);
                }

            }

        }

        public void ForegroundDraw()
        {
            if (_pixel == null || Game1.instance == null)
            {
                return;
            }

            if (_damageMarks.Count > 0)
            {
                DrawPersistentDamage();
            }

            if (_kamehamehaSeconds > 0f ||
                _genkidamaPhase == GenkidamaPhase.Charge)
            {
                PlayerEntity player = EntityManager.instance.Find<PlayerEntity>();
                if (player != null)
                {
                    Rectangle hitbox =
                        Camera.TransformRect(player.m_body.GetHitbox());

                    if (_kamehamehaSeconds > 0f)
                    {
                        DrawKamehameha(player, hitbox);
                    }

                    if (_genkidamaPhase == GenkidamaPhase.Charge)
                    {
                        DrawGenkidamaCharge(hitbox);
                    }
                }
            }

            if (_genkidamaPhase == GenkidamaPhase.Flight ||
                _genkidamaPhase == GenkidamaPhase.Explosion)
            {
                DrawGenkidamaProjectile();
            }
        }

        protected override void OnDestroy()
        {
            if (_auraFrames != null)
            {
                for (int i = 0; i < _auraFrames.Length; i++)
                {
                    if (_auraFrames[i] != null)
                    {
                        _auraFrames[i].Dispose();
                    }
                }
            }

            if (_pixel != null)
            {
                _pixel.Dispose();
            }

            _auraFrames = null;
            _pixel = null;

            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }

        private void ActivateAura()
        {
            _remainingSeconds = AuraDurationSeconds;
        }

        private void FireKamehameha()
        {
            _kamehamehaSeconds = KamehamehaDurationSeconds;
            _damageSeed = (_damageSeed + 1) % 997;
            _lastRightDamageOrigin = null;
            _lastLeftDamageOrigin = null;
            _lastKamehamehaDamageSample = -1;
        }

        private void StartGenkidama()
        {
            _genkidamaPhase = GenkidamaPhase.Charge;
            _genkidamaPhaseSeconds = 0f;
            _damageSeed = (_damageSeed + 1) % 997;
        }

        private void ProcessBrokerCommands()
        {
            BrokerCommandClient.Register(CommandTarget);

            string command;
            while (BrokerCommandClient.TryDequeue(CommandTarget, out command))
            {
                if (string.Equals(command, ActivateCommand, StringComparison.OrdinalIgnoreCase))
                {
                    ActivateAura();
                    continue;
                }

                if (string.Equals(command, KamehamehaCommand, StringComparison.OrdinalIgnoreCase))
                {
                    FireKamehameha();
                    continue;
                }

                if (string.Equals(command, GenkidamaCommand, StringComparison.OrdinalIgnoreCase))
                {
                    StartGenkidama();
                    continue;
                }

                if (string.Equals(command, DeactivateCommand, StringComparison.OrdinalIgnoreCase))
                {
                    _remainingSeconds = 0f;
                    _kamehamehaSeconds = 0f;
                    _genkidamaPhase = GenkidamaPhase.None;
                    _genkidamaPhaseSeconds = 0f;
                }
            }
        }

        private void UpdateGenkidama(float delta)
        {
            if (_genkidamaPhase == GenkidamaPhase.None)
            {
                return;
            }

            _genkidamaPhaseSeconds += delta;

            if (_genkidamaPhase == GenkidamaPhase.Charge)
            {
                PlayerEntity player = EntityManager.instance.Find<PlayerEntity>();
                if (player == null ||
                    _genkidamaPhaseSeconds < GenkidamaChargeSeconds)
                {
                    return;
                }

                Rectangle hitbox = player.m_body.GetHitbox();
                _genkidamaWorldPosition = new Vector2(
                    hitbox.Center.X,
                    hitbox.Top - 28
                );
                _genkidamaDirection = GetPlayerDirection(player);
                _genkidamaPhase = GenkidamaPhase.Flight;
                _genkidamaPhaseSeconds = 0f;
                return;
            }

            if (_genkidamaPhase == GenkidamaPhase.Flight)
            {
                _genkidamaWorldPosition.X +=
                    _genkidamaDirection * GenkidamaSpeed * delta;

                if (GenkidamaHitsTerrain() ||
                    GenkidamaReachedOffscreenExplosionPoint())
                {
                    ExplodeGenkidama();
                }

                return;
            }

            if (_genkidamaPhase == GenkidamaPhase.Explosion &&
                _genkidamaPhaseSeconds >= GenkidamaExplosionSeconds)
            {
                _genkidamaPhase = GenkidamaPhase.None;
                _genkidamaPhaseSeconds = 0f;
            }
        }

        private bool GenkidamaHitsTerrain()
        {
            Rectangle query = new Rectangle(
                (int)_genkidamaWorldPosition.X - GenkidamaRadius,
                (int)_genkidamaWorldPosition.Y - GenkidamaRadius,
                GenkidamaRadius * 2,
                GenkidamaRadius * 2
            );
            AdvCollisionInfo collisionInfo = LevelManager.GetCollisionInfo(query);
            IReadOnlyList<IBlock> blocks = collisionInfo.GetCollidedBlocks();

            for (int i = 0; i < blocks.Count; i++)
            {
                Rectangle overlap;
                if (blocks[i].Intersects(query, out overlap) ==
                    BlockCollisionType.Collision_Blocking)
                {
                    return true;
                }
            }

            return false;
        }

        private bool GenkidamaReachedOffscreenExplosionPoint()
        {
            Rectangle screenPosition = Camera.TransformRect(
                new Rectangle(
                    (int)_genkidamaWorldPosition.X,
                    (int)_genkidamaWorldPosition.Y,
                    1,
                    1
                )
            );

            return _genkidamaDirection > 0
                ? screenPosition.X >=
                    Game1.WIDTH + GenkidamaOffscreenExplosionDepth
                : screenPosition.X <= -GenkidamaOffscreenExplosionDepth;
        }

        private void ExplodeGenkidama()
        {
            _genkidamaPhase = GenkidamaPhase.Explosion;
            _genkidamaPhaseSeconds = 0f;
            CaptureGenkidamaDamage();
        }

        private void CaptureGenkidamaDamage()
        {
            int centerX = (int)_genkidamaWorldPosition.X;
            int centerY = (int)_genkidamaWorldPosition.Y;
            Rectangle query = new Rectangle(
                centerX - GenkidamaDamageRadius,
                centerY - GenkidamaDamageRadius,
                GenkidamaDamageRadius * 2,
                GenkidamaDamageRadius * 2
            );
            AdvCollisionInfo collisionInfo = LevelManager.GetCollisionInfo(query);
            IReadOnlyList<IBlock> blocks = collisionInfo.GetCollidedBlocks();

            for (int i = 0; i < blocks.Count; i++)
            {
                Rectangle overlap;
                if (blocks[i].Intersects(query, out overlap) !=
                    BlockCollisionType.Collision_Blocking)
                {
                    continue;
                }

                Rectangle damageRect = Rectangle.Intersect(overlap, query);
                if (damageRect.Width <= 0 || damageRect.Height <= 0)
                {
                    continue;
                }

                Point blastCenter = new Point(centerX, centerY);
                if (!IntersectsCircle(
                    damageRect,
                    blastCenter,
                    GenkidamaDamageRadius))
                {
                    continue;
                }

                AddBlastDamageMark(
                    damageRect,
                    blastCenter,
                    GenkidamaDamageRadius,
                    _damageSeed
                );
            }
        }

        private void CreateTextures()
        {
            GraphicsDevice graphicsDevice = Game1.instance.GraphicsDevice;
            _pixel = new Texture2D(graphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            _auraFrames = new Texture2D[6];
            for (int i = 0; i < _auraFrames.Length; i++)
            {
                _auraFrames[i] = CreateAuraFrame(graphicsDevice, 96, 128, i);
            }
        }

        private void DrawEnergyAura(Rectangle hitbox)
        {
            if (_auraFrames == null || _auraFrames.Length == 0)
            {
                return;
            }

            int frame = ((int)(_animationSeconds * 10f)) % _auraFrames.Length;
            Texture2D aura = _auraFrames[frame];
            float pulse = (float)Math.Sin(_animationSeconds * 11f) * 0.035f;
            int width = (int)(hitbox.Width * (4.7f + pulse));
            int height = (int)(hitbox.Height * (4.6f - pulse));
            int x = hitbox.Center.X - width / 2;
            int y = hitbox.Bottom - height + 10;

            Game1.spriteBatch.Draw(aura, new Rectangle(x, y, width, height), Color.White);

            for (int i = 0; i < 36; i++)
            {
                DrawOuterSpark(hitbox, i);
            }
        }

        private void DrawKamehameha(PlayerEntity player, Rectangle hitbox)
        {
            float elapsed = KamehamehaDurationSeconds - _kamehamehaSeconds;
            int direction = GetPlayerDirection(player);
            int startX = direction > 0 ? hitbox.Right + 8 : hitbox.Left - 8;
            int centerY = hitbox.Center.Y + 1;

            DrawKamehamehaCharge(startX, centerY, elapsed);

            if (elapsed < KamehamehaChargeSeconds)
            {
                return;
            }

            float fireT = Clamp01((elapsed - KamehamehaChargeSeconds) / 0.16f);
            float fadeT = Clamp01(_kamehamehaSeconds / 0.20f);
            float intensity = Math.Min(fireT, Math.Max(0.35f, fadeT));
            int length = (int)(260f + intensity * 220f);
            int coreHeight = 7 + Wave(_animationSeconds * 18f, 17, 2);
            int glowHeight = 24 + Wave(_animationSeconds * 13f, 23, 4);

            DrawBeamLayer(startX, centerY, direction, length, glowHeight, new Color((byte)28, (byte)86, (byte)214, (byte)(82 * intensity)));
            DrawBeamLayer(startX, centerY, direction, length, glowHeight / 2, new Color((byte)88, (byte)196, (byte)255, (byte)(142 * intensity)));
            DrawBeamLayer(startX, centerY, direction, length, coreHeight, new Color((byte)235, (byte)252, (byte)255, (byte)(240 * intensity)));

            for (int i = 0; i < 15; i++)
            {
                int offset = -glowHeight / 2 + (i * glowHeight) / 14;
                int jitter = Wave(_animationSeconds * (16f + i), i * 19, 5);
                int segmentLength = length - (i % 5) * 18 + Wave(_animationSeconds * 22f, i * 7, 14);
                byte alpha = (byte)(80 + (i % 4) * 22);
                Color color = i % 3 == 0
                    ? new Color((byte)255, (byte)255, (byte)255, alpha)
                    : new Color((byte)102, (byte)211, (byte)255, alpha);

                DrawBeamLine(startX, centerY + offset + jitter, direction, segmentLength, color);
            }

            DrawImpactDebris(direction, intensity);
        }

        private void DrawGenkidamaCharge(Rectangle hitbox)
        {
            float chargeT = Clamp01(
                _genkidamaPhaseSeconds / GenkidamaChargeSeconds
            );
            int radius = 5 + (int)(chargeT * 18f) +
                Wave(_animationSeconds * 11f, 71, 1);
            int centerX = hitbox.Center.X;
            int centerY = hitbox.Top - 14 - radius;
            byte glowAlpha = (byte)(75 + chargeT * 95f);
            byte coreAlpha = (byte)(155 + chargeT * 100f);

            DrawOrb(
                centerX,
                centerY,
                radius + 7,
                new Color((byte)32, (byte)105, (byte)255, glowAlpha)
            );
            DrawOrb(
                centerX,
                centerY,
                radius + 2,
                new Color((byte)75, (byte)181, (byte)255, (byte)210)
            );
            DrawOrb(
                centerX,
                centerY,
                Math.Max(2, radius - 3),
                new Color((byte)225, (byte)248, (byte)255, coreAlpha)
            );

            for (int i = 0; i < 10; i++)
            {
                float angle = _animationSeconds * (1.8f + i * 0.04f) +
                    i * 0.6283185f;
                int orbit = radius + 8 + i % 3;
                int x = centerX + (int)(Math.Cos(angle) * orbit);
                int y = centerY + (int)(Math.Sin(angle) * orbit * 0.65f);
                int size = 1 + i % 2;
                Game1.spriteBatch.Draw(
                    _pixel,
                    new Rectangle(x, y, size, size),
                    new Color((byte)126, (byte)211, (byte)255, (byte)190)
                );
            }
        }

        private void DrawGenkidamaProjectile()
        {
            Rectangle point = Camera.TransformRect(
                new Rectangle(
                    (int)_genkidamaWorldPosition.X,
                    (int)_genkidamaWorldPosition.Y,
                    1,
                    1
                )
            );
            int centerX = point.X;
            int centerY = point.Y;

            if (_genkidamaPhase == GenkidamaPhase.Flight)
            {
                int pulse = Wave(_animationSeconds * 15f, 83, 2);
                int radius = GenkidamaRadius + pulse;

                DrawOrb(
                    centerX,
                    centerY,
                    radius + 8,
                    new Color((byte)24, (byte)82, (byte)255, (byte)90)
                );
                DrawOrb(
                    centerX,
                    centerY,
                    radius + 3,
                    new Color((byte)69, (byte)174, (byte)255, (byte)215)
                );
                DrawOrb(
                    centerX,
                    centerY,
                    Math.Max(3, radius - 5),
                    new Color((byte)234, (byte)251, (byte)255, (byte)250)
                );
                return;
            }

            float explosionT = Clamp01(
                _genkidamaPhaseSeconds / GenkidamaExplosionSeconds
            );
            int explosionRadius = GenkidamaRadius +
                (int)(explosionT * GenkidamaDamageRadius);
            float rayFade = 1f - Clamp01((explosionT - 0.42f) / 0.18f);

            for (int i = 0; i < 28; i++)
            {
                float angle = i * 0.2243995f +
                    DamageValue(_damageSeed, i, 109, 100) * 0.0018f;
                int innerRadius = Math.Max(4, explosionRadius / 3);
                int rayLength = explosionRadius +
                    14 +
                    DamageValue(_damageSeed, i, 113, 48);
                int startX = centerX + (int)(Math.Cos(angle) * innerRadius);
                int startY = centerY + (int)(Math.Sin(angle) * innerRadius);
                int endX = centerX + (int)(Math.Cos(angle) * rayLength);
                int endY = centerY + (int)(Math.Sin(angle) * rayLength);
                byte rayAlpha = (byte)(
                    (110 + DamageValue(_damageSeed, i, 127, 100)) *
                    (1f - explosionT) *
                    rayFade
                );
                Color rayColor = i % 3 == 0
                    ? new Color(
                        (byte)230,
                        (byte)250,
                        (byte)255,
                        rayAlpha
                    )
                    : new Color(
                        (byte)55,
                        (byte)143,
                        (byte)255,
                        rayAlpha
                    );

                DrawDebrisLine(startX, startY, endX, endY, rayColor);
            }

            if (explosionT < 0.55f)
            {
                DrawOrb(
                    centerX,
                    centerY,
                    explosionRadius + 12,
                    new Color(
                        (byte)22,
                        (byte)78,
                        (byte)255,
                        (byte)(150f * (1f - explosionT))
                    )
                );
                DrawOrb(
                    centerX,
                    centerY,
                    explosionRadius,
                    new Color(
                        (byte)74,
                        (byte)185,
                        (byte)255,
                        (byte)(225f * (1f - explosionT * 0.75f))
                    )
                );
                DrawOrb(
                    centerX,
                    centerY,
                    Math.Max(3, explosionRadius / 2),
                    new Color(
                        (byte)238,
                        (byte)252,
                        (byte)255,
                        (byte)(255f * (1f - explosionT))
                    )
                );
                return;
            }

            DrawDissolvingExplosion(
                centerX,
                centerY,
                explosionRadius + 12,
                Clamp01((explosionT - 0.55f) / 0.45f)
            );
        }

        private void DrawDissolvingExplosion(
            int centerX,
            int centerY,
            int radius,
            float dissolveT
        )
        {
            int chunkIndex = 0;
            for (int y = -radius; y <= radius; y += 3)
            {
                float normalizedY = y / (float)radius;
                int halfWidth = (int)Math.Round(
                    Math.Sqrt(Math.Max(
                        0f,
                        1f - normalizedY * normalizedY
                    )) * radius
                );
                int x = -halfWidth;

                while (x <= halfWidth)
                {
                    int width = 5 +
                        DamageValue(_damageSeed, chunkIndex, 131, 9);
                    int remaining = halfWidth - x + 1;
                    width = Math.Min(width, remaining);
                    float lifetime = 0.16f +
                        DamageValue(
                            _damageSeed,
                            chunkIndex,
                            137,
                            84
                        ) / 100f;

                    if (dissolveT < lifetime)
                    {
                        float localFade = Clamp01(
                            (lifetime - dissolveT) / 0.14f
                        );
                        float chunkCenterX = x + width * 0.5f;
                        float distance = (float)Math.Sqrt(
                            chunkCenterX * chunkCenterX + y * y
                        );
                        float proximity = 1f - Clamp01(distance / radius);
                        byte alpha = (byte)(225f * localFade);
                        Color color = proximity > 0.58f
                            ? new Color(
                                (byte)238,
                                (byte)252,
                                (byte)255,
                                alpha
                            )
                            : proximity > 0.24f
                                ? new Color(
                                    (byte)74,
                                    (byte)185,
                                    (byte)255,
                                    alpha
                                )
                                : new Color(
                                    (byte)22,
                                    (byte)78,
                                    (byte)255,
                                    alpha
                                );

                        Game1.spriteBatch.Draw(
                            _pixel,
                            new Rectangle(
                                centerX + x,
                                centerY + y,
                                width,
                                Math.Min(3, radius - y + 1)
                            ),
                            color
                        );
                    }

                    x += width;
                    chunkIndex++;
                }
            }
        }

        private void UpdateDamageScreen()
        {
            LevelScreen screen = LevelManager.CurrentScreen;
            int screenIndex = screen == null ? -1 : screen.GetIndex0();

            if (screenIndex == _damageScreenIndex)
            {
                return;
            }

            _damageScreenIndex = screenIndex;
            _damageMarks.Clear();
            _lastRightDamageOrigin = null;
            _lastLeftDamageOrigin = null;
            _lastKamehamehaDamageSample = -1;
        }

        private void CaptureKamehamehaDamage()
        {
            float elapsed = KamehamehaDurationSeconds - _kamehamehaSeconds;
            if (elapsed < KamehamehaChargeSeconds)
            {
                return;
            }

            PlayerEntity player = EntityManager.instance.Find<PlayerEntity>();
            LevelScreen screen = LevelManager.CurrentScreen;
            if (screen == null || player == null)
            {
                return;
            }

            Rectangle playerHitbox = player.m_body.GetHitbox();
            int direction = GetPlayerDirection(player);
            int startX = direction > 0 ? playerHitbox.Right + 8 : playerHitbox.Left - 8;
            int centerY = playerHitbox.Center.Y + 1;
            Point origin = new Point(startX, centerY);
            Point? previousOrigin = direction > 0
                ? _lastRightDamageOrigin
                : _lastLeftDamageOrigin;
            bool originMoved =
                !previousOrigin.HasValue ||
                Math.Abs(previousOrigin.Value.X - origin.X) >= 12 ||
                Math.Abs(previousOrigin.Value.Y - origin.Y) >= 6;
            int damageSample = (int)(
                (elapsed - KamehamehaChargeSeconds) /
                KamehamehaDamageSampleSeconds
            );

            if (!originMoved &&
                damageSample <= _lastKamehamehaDamageSample)
            {
                return;
            }

            _lastKamehamehaDamageSample = Math.Max(
                _lastKamehamehaDamageSample,
                damageSample
            );

            if (direction > 0)
            {
                _lastRightDamageOrigin = origin;
            }
            else
            {
                _lastLeftDamageOrigin = origin;
            }

            int queryX = direction > 0 ? startX : startX - KamehamehaMaximumLength;
            Rectangle query = new Rectangle(
                queryX,
                centerY - KamehamehaCollisionHeight / 2,
                KamehamehaMaximumLength,
                KamehamehaCollisionHeight
            );

            if (query.Width <= 0 || query.Height <= 0)
            {
                return;
            }

            AdvCollisionInfo collisionInfo = LevelManager.GetCollisionInfo(query);
            IReadOnlyList<IBlock> blocks = collisionInfo.GetCollidedBlocks();

            for (int i = 0; i < blocks.Count; i++)
            {
                Rectangle overlap;
                if (blocks[i].Intersects(query, out overlap) !=
                    BlockCollisionType.Collision_Blocking)
                {
                    continue;
                }

                Rectangle damageRect = Rectangle.Intersect(overlap, query);
                if (damageRect.Width > 0 && damageRect.Height > 0)
                {
                    AddDamageMark(damageRect, direction, _damageSeed);
                }
            }
        }

        private void AddDamageMark(Rectangle worldRect, int direction, int seed)
        {
            for (int i = 0; i < _damageMarks.Count; i++)
            {
                DamageMark existing = _damageMarks[i];
                if (existing.Direction == direction &&
                    existing.WorldRect.Intersects(worldRect))
                {
                    int left = Math.Min(existing.WorldRect.Left, worldRect.Left);
                    int top = Math.Min(existing.WorldRect.Top, worldRect.Top);
                    int right = Math.Max(existing.WorldRect.Right, worldRect.Right);
                    int bottom = Math.Max(existing.WorldRect.Bottom, worldRect.Bottom);
                    Rectangle combined = new Rectangle(
                        left,
                        top,
                        right - left,
                        bottom - top
                    );
                    _damageMarks[i] = new DamageMark(
                        combined,
                        direction,
                        existing.Seed,
                        Math.Min(32, existing.Exposure + 1)
                    );
                    return;
                }
            }

            int markSeed = unchecked(
                seed ^
                worldRect.X * 73856093 ^
                worldRect.Y * 19349663 ^
                worldRect.Width * 83492791 ^
                worldRect.Height * 265443576
            );

            _damageMarks.Add(new DamageMark(worldRect, direction, markSeed, 1));
        }

        private void AddBlastDamageMark(
            Rectangle worldRect,
            Point center,
            int radius,
            int seed
        )
        {
            int markSeed = unchecked(
                seed ^
                worldRect.X * 73856093 ^
                worldRect.Y * 19349663 ^
                worldRect.Width * 83492791 ^
                worldRect.Height * 265443576
            );

            _damageMarks.Add(
                new DamageMark(worldRect, center, radius, markSeed)
            );
        }

        private static bool IntersectsCircle(
            Rectangle rect,
            Point center,
            int radius
        )
        {
            int nearestX = Math.Max(rect.Left, Math.Min(center.X, rect.Right));
            int nearestY = Math.Max(rect.Top, Math.Min(center.Y, rect.Bottom));
            int dx = nearestX - center.X;
            int dy = nearestY - center.Y;
            return dx * dx + dy * dy <= radius * radius;
        }

        private void DrawPersistentDamage()
        {
            for (int i = 0; i < _damageMarks.Count; i++)
            {
                DamageMark mark = _damageMarks[i];
                Rectangle rect = Camera.TransformRect(mark.WorldRect);
                if (mark.Shape == DamageShape.Blast)
                {
                    Point center = Camera.TransformRect(
                        new Rectangle(mark.BlastCenter.X, mark.BlastCenter.Y, 1, 1)
                    ).Location;
                    DrawExplosionDamage(rect, center, mark.Radius, mark.Seed);
                }
                else
                {
                    DrawDestroyedSurface(
                        rect,
                        mark.Direction,
                        mark.Seed,
                        mark.Exposure
                    );
                }
            }
        }

        private void DrawDestroyedSurface(
            Rectangle rect,
            int direction,
            int seed,
            int exposure
        )
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            Color hole = new Color((byte)3, (byte)3, (byte)3, (byte)230);
            Color soot = new Color((byte)12, (byte)10, (byte)8, (byte)220);
            Color charred = new Color((byte)29, (byte)20, (byte)14, (byte)215);
            Color ash = new Color((byte)47, (byte)36, (byte)27, (byte)175);
            Color scorchedBrown = new Color(
                (byte)55,
                (byte)32,
                (byte)20,
                (byte)170
            );
            int fullDepth = Math.Max(
                1,
                Math.Min(rect.Width, Math.Max(4, rect.Width * 3 / 4))
            );
            int exposurePercent = Math.Min(88, 8 + exposure * 4);
            int burnChance = Math.Min(68, 4 + exposure * 3);
            int maximumDepth = Math.Max(
                1,
                fullDepth * exposurePercent / 100
            );
            int strip = 0;
            int y = rect.Top;
            int depth = Math.Min(
                maximumDepth,
                2 + DamageValue(seed, 0, 5, Math.Max(1, maximumDepth))
            );

            while (y < rect.Bottom)
            {
                int stripHeight = Math.Min(
                    rect.Bottom - y,
                    1 + DamageValue(seed, strip, 7, 4)
                );
                int depthChange = DamageValue(seed, strip, 11, 7) - 3;
                depth = Math.Max(1, Math.Min(maximumDepth, depth + depthChange));

                if (DamageValue(seed, strip, 13, 100) < burnChance)
                {
                    int raggedDepth = Math.Max(
                        1,
                        Math.Min(
                            maximumDepth,
                            depth + DamageValue(seed, strip, 17, 5) - 2
                        )
                    );
                    int x = direction > 0
                        ? rect.Left
                        : rect.Right - raggedDepth;
                    int colorRoll = DamageValue(seed, strip, 19, 100);
                    Color burnColor = colorRoll < 48
                        ? hole
                        : colorRoll < 78
                            ? soot
                            : colorRoll < 94
                                ? charred
                                : ash;

                    Game1.spriteBatch.Draw(
                        _pixel,
                        new Rectangle(x, y, raggedDepth, stripHeight),
                        burnColor
                    );
                }

                y += stripHeight;
                strip++;
            }

            int maximumPits = Math.Max(
                3,
                Math.Min(12, rect.Width * rect.Height / 48)
            );
            int pits = Math.Max(1, maximumPits * exposurePercent / 100);
            for (int i = 0; i < pits; i++)
            {
                int pitWidth = Math.Min(
                    rect.Width,
                    1 + DamageValue(seed, i, 23, 4)
                );
                int pitHeight = Math.Min(
                    rect.Height,
                    1 + DamageValue(seed, i, 29, 3)
                );
                int pitDepth = 1 + DamageValue(seed, i, 31, maximumDepth);
                int pitX = direction > 0
                    ? Math.Min(rect.Right - pitWidth, rect.Left + pitDepth - 1)
                    : Math.Max(rect.Left, rect.Right - pitDepth - pitWidth + 1);
                int pitY = rect.Top + DamageValue(
                    seed,
                    i,
                    37,
                    Math.Max(1, rect.Height - pitHeight + 1)
                );

                Game1.spriteBatch.Draw(
                    _pixel,
                    new Rectangle(pitX, pitY, pitWidth, pitHeight),
                    DamageValue(seed, i, 41, 5) == 0
                        ? scorchedBrown
                        : hole
                );
            }

            if (rect.Width <= 1)
            {
                return;
            }

            int maximumCracks = Math.Max(2, Math.Min(7, rect.Height / 6 + 1));
            int cracks = Math.Max(1, maximumCracks * exposurePercent / 100);
            int surfaceX = direction > 0 ? rect.Left : rect.Right - 1;
            for (int i = 0; i < cracks; i++)
            {
                int crackY = rect.Top + DamageValue(seed, i, 43, rect.Height);
                int crackDepth = Math.Min(
                    Math.Max(0, rect.Width - 1),
                    3 + DamageValue(seed, i, 47, Math.Max(1, rect.Width / 2 + 5))
                );
                int bendX = surfaceX + direction * Math.Max(1, crackDepth / 3);
                int bendY = crackY + DamageValue(seed, i, 53, 9) - 4;
                int endX = surfaceX + direction * crackDepth;
                int endY = bendY + DamageValue(seed, i, 59, 11) - 5;

                DrawDebrisLine(
                    surfaceX,
                    crackY,
                    bendX,
                    bendY,
                    scorchedBrown
                );
                DrawDebrisLine(bendX, bendY, endX, endY, hole);

                if (i % 2 == 0 && crackDepth >= 5)
                {
                    int branchX = bendX + direction * Math.Max(1, crackDepth / 3);
                    int branchY = bendY + (
                        DamageValue(seed, i, 61, 2) == 0 ? -3 : 3
                    );
                    DrawDebrisLine(bendX, bendY, branchX, branchY, soot);
                }
            }
        }

        private void DrawExplosionDamage(
            Rectangle rect,
            Point center,
            int radius,
            int seed
        )
        {
            if (rect.Width <= 0 || rect.Height <= 0 || radius <= 0)
            {
                return;
            }

            Color hole = new Color((byte)3, (byte)3, (byte)3, (byte)230);
            Color soot = new Color((byte)12, (byte)10, (byte)8, (byte)220);
            Color charred = new Color((byte)29, (byte)20, (byte)14, (byte)215);
            Color ash = new Color((byte)47, (byte)36, (byte)27, (byte)175);
            Color scorchedBrown = new Color(
                (byte)55,
                (byte)32,
                (byte)20,
                (byte)170
            );
            int samples = Math.Max(
                10,
                Math.Min(64, rect.Width * rect.Height / 36)
            );
            float radiusSquared = radius * radius;

            for (int i = 0; i < samples; i++)
            {
                int x = rect.Left + DamageValue(seed, i, 71, rect.Width);
                int y = rect.Top + DamageValue(seed, i, 73, rect.Height);
                float dx = x - center.X;
                float dy = y - center.Y;
                float distanceSquared = dx * dx + dy * dy;

                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                float distance = (float)Math.Sqrt(distanceSquared);
                float proximity = 1f - distance / radius;
                int chance = 12 + (int)(proximity * 78f);
                if (DamageValue(seed, i, 79, 100) >= chance)
                {
                    continue;
                }

                int maximumSize = 2 + (int)(proximity * 6f);
                int patchWidth = Math.Min(
                    rect.Right - x,
                    1 + DamageValue(seed, i, 83, Math.Max(1, maximumSize))
                );
                int patchHeight = Math.Min(
                    rect.Bottom - y,
                    1 + DamageValue(seed, i, 89, Math.Max(1, maximumSize / 2 + 1))
                );
                int colorRoll = DamageValue(seed, i, 97, 100);
                Color color = proximity > 0.68f
                    ? (colorRoll < 68 ? hole : soot)
                    : proximity > 0.32f
                        ? (colorRoll < 52 ? soot : charred)
                        : (colorRoll < 58 ? ash : scorchedBrown);

                if (patchWidth > 0 && patchHeight > 0)
                {
                    Game1.spriteBatch.Draw(
                        _pixel,
                        new Rectangle(x, y, patchWidth, patchHeight),
                        color
                    );
                }
            }
        }

        private void DrawImpactDebris(int direction, float intensity)
        {
            for (int i = 0; i < _damageMarks.Count; i++)
            {
                DamageMark mark = _damageMarks[i];
                if (mark.Seed != _damageSeed || mark.Direction != direction)
                {
                    continue;
                }

                Rectangle rect = Camera.TransformRect(mark.WorldRect);
                int surfaceX = direction > 0 ? rect.Left : rect.Right;

                for (int fragment = 0; fragment < 4; fragment++)
                {
                    float phase = (
                        _animationSeconds * 2.8f +
                        DamageValue(mark.Seed, i + fragment, 29, 100) / 100f
                    ) % 1f;
                    int distance = 5 + (int)(phase * 34f);
                    int side = DamageValue(mark.Seed, fragment, 31, 2) == 0 ? -1 : 1;
                    int x = surfaceX - direction * distance;
                    int y = rect.Center.Y + side * (
                        2 + (int)(phase * (8 + DamageValue(mark.Seed, fragment, 37, 18)))
                    );
                    int size = 2 + DamageValue(mark.Seed, fragment, 41, 3);
                    byte alpha = (byte)(190f * intensity * (1f - phase * 0.55f));

                    Game1.spriteBatch.Draw(
                        _pixel,
                        new Rectangle(x, y, size, Math.Max(1, size - 1)),
                        new Color((byte)34, (byte)22, (byte)13, alpha)
                    );
                }
            }
        }

        private static int DamageValue(int seed, int index, int salt, int range)
        {
            if (range <= 1)
            {
                return 0;
            }

            int value = seed * 73856093;
            value ^= (index + 1) * 19349663;
            value ^= salt * 83492791;
            value ^= value >> 13;
            value *= 1274126177;
            value ^= value >> 16;

            return (value & int.MaxValue) % range;
        }

        private enum GenkidamaPhase
        {
            None,
            Charge,
            Flight,
            Explosion
        }

        private enum DamageShape
        {
            Beam,
            Blast
        }

        private struct DamageMark
        {
            public readonly Rectangle WorldRect;
            public readonly int Direction;
            public readonly int Seed;
            public readonly int Exposure;
            public readonly DamageShape Shape;
            public readonly Point BlastCenter;
            public readonly int Radius;

            public DamageMark(
                Rectangle worldRect,
                int direction,
                int seed,
                int exposure
            )
            {
                WorldRect = worldRect;
                Direction = direction;
                Seed = seed;
                Exposure = exposure;
                Shape = DamageShape.Beam;
                BlastCenter = Point.Zero;
                Radius = 0;
            }

            public DamageMark(
                Rectangle worldRect,
                Point blastCenter,
                int radius,
                int seed
            )
            {
                WorldRect = worldRect;
                Direction = 0;
                Seed = seed;
                Exposure = 1;
                Shape = DamageShape.Blast;
                BlastCenter = blastCenter;
                Radius = radius;
            }
        }

        private void DrawDebrisLine(int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                Game1.spriteBatch.Draw(_pixel, new Rectangle(x0, y0, 1, 1), color);

                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = err * 2;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private int GetPlayerDirection(PlayerEntity player)
        {
            if (PlayerFlipField != null)
            {
                object value = PlayerFlipField.GetValue(player);
                if (value is SpriteEffects effects)
                {
                    return (effects & SpriteEffects.FlipHorizontally) != 0 ? -1 : 1;
                }
            }

            return _lastHorizontalDirection < 0 ? -1 : 1;
        }

        private void DrawKamehamehaCharge(int startX, int centerY, float elapsed)
        {
            float chargeT = Clamp01(elapsed / KamehamehaChargeSeconds);
            int radius = 4 + (int)(chargeT * 7f) + Wave(_animationSeconds * 18f, 31, 1);
            byte alpha = (byte)(100 + chargeT * 120f);

            DrawOrb(startX, centerY, radius + 5, new Color((byte)25, (byte)83, (byte)211, (byte)(alpha * 0.38f)));
            DrawOrb(startX, centerY, radius + 2, new Color((byte)91, (byte)202, (byte)255, (byte)(alpha * 0.64f)));
            DrawOrb(startX, centerY, radius, new Color((byte)235, (byte)252, (byte)255, alpha));
        }

        private void DrawBeamLayer(int startX, int centerY, int direction, int length, int height, Color color)
        {
            int y = centerY - height / 2;
            int x = direction > 0 ? startX : startX - length;
            Game1.spriteBatch.Draw(_pixel, new Rectangle(x, y, length, height), color);
        }

        private void DrawBeamLine(int startX, int y, int direction, int length, Color color)
        {
            int x = direction > 0 ? startX : startX - length;
            Game1.spriteBatch.Draw(_pixel, new Rectangle(x, y, length, 1), color);
        }

        private void DrawOrb(int centerX, int centerY, int radius, Color color)
        {
            for (int y = -radius; y <= radius; y++)
            {
                float normalizedY = y / (float)radius;
                int halfWidth = (int)Math.Round(
                    Math.Sqrt(Math.Max(0f, 1f - normalizedY * normalizedY)) * radius
                );
                Game1.spriteBatch.Draw(
                    _pixel,
                    new Rectangle(centerX - halfWidth, centerY + y, halfWidth * 2 + 1, 1),
                    color
                );
            }
        }

        private void TrackHorizontalDirection(KeyboardState keyboardState)
        {
            if (keyboardState.IsKeyDown(Keys.Left) || keyboardState.IsKeyDown(Keys.J))
            {
                _lastHorizontalDirection = -1;
            }

            if (keyboardState.IsKeyDown(Keys.Right) || keyboardState.IsKeyDown(Keys.K))
            {
                _lastHorizontalDirection = 1;
            }
        }

        private Texture2D CreateAuraFrame(GraphicsDevice graphicsDevice, int width, int height, int frame)
        {
            Texture2D texture = new Texture2D(graphicsDevice, width, height);
            Color[] pixels = new Color[width * height];
            int centerX = width / 2;

            for (int y = 0; y < height; y++)
            {
                float vertical = y / (float)(height - 1);
                float leftEdge = FlameEdgeX(y, frame, -1, height, centerX);
                float rightEdge = FlameEdgeX(y, frame, 1, height, centerX);

                for (int x = 0; x < width; x++)
                {
                    float dx = Math.Abs(x - centerX);
                    float edge = Math.Min(x - leftEdge, rightEdge - x);

                    if (edge < 0f)
                    {
                        pixels[y * width + x] = Color.Transparent;
                        continue;
                    }

                    float edgeGlow = Clamp01(1f - edge / 5.8f);
                    float bodyGlow = Clamp01(edge / 20f);
                    float topHeat = Clamp01(1f - vertical * 0.68f);
                    float flicker = (float)Math.Sin((x + frame * 13) * 0.19f + y * 0.13f) * 0.035f;
                    float alpha = 0.025f + edgeGlow * 0.74f + bodyGlow * 0.035f + topHeat * 0.025f + flicker;

                    alpha *= CharacterPaintFactor(x, y, width, height);

                    if (alpha <= 0.025f)
                    {
                        pixels[y * width + x] = Color.Transparent;
                        continue;
                    }

                    int noise = (x * 17 + y * 31 + frame * 13) & 7;
                    alpha = Clamp01(alpha + (noise - 3) * 0.007f);
                    float sparkle = Clamp01((flicker + 0.035f) / 0.07f);
                    float rim = Clamp01(edgeGlow * 1.08f);
                    float body = Clamp01(bodyGlow);
                    float heat = Clamp01(rim * 0.62f + body * 0.22f + sparkle * 0.20f + topHeat * 0.16f);
                    float ember = ((noise == 0 || noise == 5) && body > 0.18f && rim < 0.72f) ? 1f : 0f;

                    byte a = (byte)(alpha * 255f);
                    byte r = (byte)(255f - ember * 58f);
                    byte g = (byte)Math.Min(255f, 120f + body * 28f + rim * 82f + heat * 42f - ember * 58f);
                    byte b = (byte)Math.Min(165f, 4f + body * 10f + rim * 34f + heat * 68f - ember * 18f);

                    if (rim > 0.82f && sparkle > 0.55f)
                    {
                        g = (byte)Math.Min(255, g + 18);
                        b = (byte)Math.Min(185, b + 24);
                    }

                    pixels[y * width + x] = new Color(r, g, b, a);
                }
            }

            DrawCrackleBolts(pixels, width, height, centerX, frame);

            texture.SetData(pixels);
            return texture;
        }

        private void DrawCrackleBolts(Color[] pixels, int width, int height, int centerX, int frame)
        {
            for (int i = 0; i < 7; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                int y = 18 + i * 13 + (frame * 5 + i * 7) % 9;
                int x = centerX + side * (10 + (i * 5 + frame * 3) % 22);
                int segments = 4 + i % 3;

                for (int s = 0; s < segments; s++)
                {
                    int nextY = y + 7 + (s * 3 + i) % 6;
                    int nextX = x + side * (3 + (s * 5 + frame + i) % 7) - side * ((s % 2) * 5);
                    DrawCrackleLine(pixels, width, height, x, y, nextX, nextY);
                    x = nextX;
                    y = nextY;
                }
            }
        }

        private void DrawCrackleLine(Color[] pixels, int width, int height, int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                AddCracklePixel(pixels, width, height, x0 - 1, y0, new Color((byte)22, (byte)10, (byte)0, (byte)105));
                AddCracklePixel(pixels, width, height, x0, y0, new Color((byte)88, (byte)42, (byte)0, (byte)150));
                AddCracklePixel(pixels, width, height, x0 + 1, y0, new Color((byte)255, (byte)232, (byte)72, (byte)95));

                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = err * 2;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private void AddCracklePixel(Color[] pixels, int width, int height, int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            int index = y * width + x;
            if (pixels[index].A == 0)
            {
                return;
            }

            pixels[index] = color;
        }

        private float FlameEdgeX(int y, int frame, int side, int height, int centerX)
        {
            int[] pointsY = new int[]
            {
                2, 7, 12, 17, 22, 27, 32, 37,
                42, 47, 52, 57, 62, 67, 72, 77,
                82, 87, 92, 97, 102, 107, 112, 117,
                122, 127
            };
            float[] offsets = new float[]
            {
                1f, 9f, 4f, 16f, 8f, 24f, 14f, 32f,
                22f, 38f, 25f, 43f, 30f, 39f, 24f, 42f,
                29f, 37f, 23f, 35f, 21f, 31f, 15f, 25f,
                10f, 17f
            };

            if (y <= pointsY[0])
            {
                return centerX + side * offsets[0];
            }

            for (int i = 0; i < pointsY.Length - 1; i++)
            {
                int y0 = pointsY[i];
                int y1 = pointsY[i + 1];
                if (y <= y1)
                {
                    float t = (y - y0) / (float)(y1 - y0);
                    float offset = Lerp(offsets[i], offsets[i + 1], t);
                    offset += (float)Math.Sin(y * 0.41f + frame * 0.9f + side * 1.7f) * 0.9f;
                    return centerX + side * offset;
                }
            }

            return centerX + side * offsets[offsets.Length - 1];
        }

        private float CharacterPaintFactor(int x, int y, int width, int height)
        {
            float vertical = y / (float)(height - 1);
            if (vertical < 0.62f)
            {
                return 1f;
            }

            float centerX = width * 0.5f;
            float dx = Math.Abs(x - centerX);
            float t = Clamp01((vertical - 0.62f) / 0.36f);
            float widthCurve = (float)Math.Sin(t * Math.PI);
            float jagged = ((y / 4) % 2 == 0 ? 2.4f : -2.1f) + (float)Math.Sin(y * 0.83f) * 1.0f;
            float maskWidth = 4f + widthCurve * 10f + t * 5f + jagged;

            if (dx < maskWidth)
            {
                return 0.02f;
            }

            if (dx < maskWidth + 2f)
            {
                return Clamp01((dx - maskWidth) / 2f);
            }

            return 1f;
        }
        private void DrawOuterSpark(Rectangle hitbox, int index)
        {
            int lane = index / 2;
            int side = index % 2 == 0 ? -1 : 1;
            bool shortSpark = index % 4 == 0;
            float travel = shortSpark ? 48f : 78f;
            float speed = 68f + (index % 9) * 9f;
            float yOffset = (_animationSeconds * speed + index * 17f) % travel;
            int spread = hitbox.Width / 2 + 12 + (lane % 12) * 5;
            int wobble = Wave(_animationSeconds * 5f, index + 90, 2);
            int x = hitbox.Center.X + side * spread + wobble;
            int bottomLimit = hitbox.Bottom - 5 + (index * 7) % 15;
            int top = bottomLimit - (int)yOffset;
            int height = shortSpark ? 10 + (index % 5) * 4 : 22 + (index % 7) * 6;
            int bottom = Math.Min(bottomLimit, top + height);

            if (bottom <= top)
            {
                return;
            }

            byte alpha = (byte)(70 + (index % 6) * 18);
            Color dark = new Color((byte)18, (byte)8, (byte)0, (byte)(alpha * 0.24f));
            Color shadow = new Color((byte)255, (byte)118, (byte)4, (byte)(alpha * 0.36f));
            Color core = new Color((byte)255, (byte)230, (byte)58, alpha);
            int visibleHeight = bottom - top;
            Game1.spriteBatch.Draw(_pixel, new Rectangle(x - 2, top + visibleHeight / 4, 1, Math.Max(1, visibleHeight / 2)), dark);
            Game1.spriteBatch.Draw(_pixel, new Rectangle(x - 1, top + visibleHeight / 5, 1, Math.Max(1, visibleHeight * 3 / 5)), shadow);
            Game1.spriteBatch.Draw(_pixel, new Rectangle(x, top, 1, visibleHeight), core);
        }
        private float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        private float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * amount;
        }

        private int Wave(float time, int index, int amount)
        {
            return (int)(Math.Sin(time + index * 1.31f) * amount);
        }

        private bool WasKeyPressed(KeyboardState keyboardState, Keys key)
        {
            return keyboardState.IsKeyDown(key) &&
                !_previousKeyboardState.IsKeyDown(key);
        }
    }
    internal static class BrokerCommandClient
    {
        private const string RegistryTypeName = "JumpKingHttpCommandBroker.CommandQueueRegistry";

        private static object _registry;
        private static MethodInfo _registerMethod;
        private static MethodInfo _tryDequeueMethod;
        private static DateTime _nextResolveUtc = DateTime.MinValue;
        private static bool _loggedMissingBroker;
        private static bool _registered;

        public static void Register(string target)
        {
            if (_registered)
            {
                return;
            }

            if (!Resolve())
            {
                return;
            }

            try
            {
                _registerMethod.Invoke(_registry, new object[] { target });
                _registered = true;
            }
            catch (Exception ex)
            {
                JumpKing.Program.crashLog.AddErrorMessage(
                    "SuperSaiyan broker register failed: " + ex.Message
                );
            }
        }

        public static bool TryDequeue(string target, out string command)
        {
            command = null;

            if (!_registered)
            {
                Register(target);
            }

            if (!_registered || !Resolve())
            {
                return false;
            }

            try
            {
                object[] args = new object[] { target, null };
                bool dequeued = (bool)_tryDequeueMethod.Invoke(_registry, args);
                command = args[1] as string;
                return dequeued;
            }
            catch (Exception ex)
            {
                JumpKing.Program.crashLog.AddErrorMessage(
                    "SuperSaiyan broker dequeue failed: " + ex.Message
                );
                return false;
            }
        }

        private static bool Resolve()
        {
            if (_registry != null)
            {
                return true;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextResolveUtc)
            {
                return false;
            }

            _nextResolveUtc = nowUtc.AddSeconds(1);

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type registryType = assemblies[i].GetType(RegistryTypeName, false);
                if (registryType == null)
                {
                    continue;
                }

                FieldInfo instanceField = registryType.GetField(
                    "Instance",
                    BindingFlags.Public | BindingFlags.Static
                );
                MethodInfo registerMethod = registryType.GetMethod(
                    "Register",
                    new Type[] { typeof(string) }
                );
                MethodInfo tryDequeueMethod = registryType.GetMethod(
                    "TryDequeue",
                    new Type[] { typeof(string), typeof(string).MakeByRefType() }
                );

                if (instanceField == null || registerMethod == null || tryDequeueMethod == null)
                {
                    continue;
                }

                _registry = instanceField.GetValue(null);
                _registerMethod = registerMethod;
                _tryDequeueMethod = tryDequeueMethod;
                return _registry != null;
            }

            if (!_loggedMissingBroker)
            {
                _loggedMissingBroker = true;
                JumpKing.Program.crashLog.AddErrorMessage(
                    "SuperSaiyan: JumpKingHttpCommandBroker is not loaded."
                );
            }

            return false;
        }
    }
}
