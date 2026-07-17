using System;
using System.Reflection;
using EntityComponent;
using JumpKing;
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

    public sealed class SuperSaiyanAura : Entity, JumpKing.Util.IDrawable
    {
        private const float AuraDurationSeconds = 20f;
        private const float KamehamehaChargeSeconds = 1f;
        private const float KamehamehaBeamSeconds = 5f;
        private const float KamehamehaDurationSeconds =
            KamehamehaChargeSeconds + KamehamehaBeamSeconds;
        private const string CommandTarget = "super_saiyan";
        private const string ActivateCommand = "activate";
        private const string KamehamehaCommand = "kamehameha";
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
        private int _lastHorizontalDirection = 1;

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

            _previousKeyboardState = keyboardState;



            if (_remainingSeconds > 0f)
            {
                _remainingSeconds = Math.Max(0f, _remainingSeconds - delta);
            }

            if (_kamehamehaSeconds > 0f)
            {
                _kamehamehaSeconds = Math.Max(0f, _kamehamehaSeconds - delta);
            }
        }

        public override void Draw()
        {
            if ((_remainingSeconds <= 0f && _kamehamehaSeconds <= 0f) ||
                _pixel == null)
            {
                return;
            }

            PlayerEntity player = EntityManager.instance.Find<PlayerEntity>();

            if (player == null || Game1.instance == null)
            {
                return;
            }

            Rectangle hitbox = Camera.TransformRect(player.m_body.GetHitbox());

            if (_remainingSeconds > 0f)
            {
                DrawEnergyAura(hitbox);
            }

            if (_kamehamehaSeconds > 0f)
            {
                DrawKamehameha(player, hitbox);
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

                if (string.Equals(command, DeactivateCommand, StringComparison.OrdinalIgnoreCase))
                {
                    _remainingSeconds = 0f;
                    _kamehamehaSeconds = 0f;
                }
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

            DrawBeamLayer(startX, centerY, direction, length, glowHeight, new Color((byte)214, (byte)126, (byte)18, (byte)(78 * intensity)));
            DrawBeamLayer(startX, centerY, direction, length, glowHeight / 2, new Color((byte)255, (byte)214, (byte)48, (byte)(132 * intensity)));
            DrawBeamLayer(startX, centerY, direction, length, coreHeight, new Color((byte)255, (byte)255, (byte)206, (byte)(235 * intensity)));
            DrawKamehamehaDamage(startX, centerY, direction, length, glowHeight, intensity);

            for (int i = 0; i < 15; i++)
            {
                int offset = -glowHeight / 2 + (i * glowHeight) / 14;
                int jitter = Wave(_animationSeconds * (16f + i), i * 19, 5);
                int segmentLength = length - (i % 5) * 18 + Wave(_animationSeconds * 22f, i * 7, 14);
                byte alpha = (byte)(80 + (i % 4) * 22);
                Color color = i % 3 == 0
                    ? new Color((byte)255, (byte)255, (byte)255, alpha)
                    : new Color((byte)255, (byte)214, (byte)42, alpha);

                DrawBeamLine(startX, centerY + offset + jitter, direction, segmentLength, color);
            }
        }

        private void DrawKamehamehaDamage(
            int startX,
            int centerY,
            int direction,
            int length,
            int beamHeight,
            float intensity
        )
        {
            int left = direction > 0 ? startX : startX - length;
            int top = centerY - beamHeight / 2;
            int bottom = centerY + beamHeight / 2;
            int alpha = (int)(120 * intensity);

            for (int i = 0; i < 28; i++)
            {
                int along = 10 + (i * 37 + Wave(_animationSeconds * 23f, i * 11, 9)) % Math.Max(1, length - 20);
                int x = direction > 0 ? startX + along : startX - along;
                int side = i % 2 == 0 ? -1 : 1;
                int edgeY = side < 0 ? top : bottom;
                int y = edgeY + side * (2 + (i * 5) % 13);
                int width = 5 + (i % 5) * 3;
                int height = 1 + (i % 3);
                byte smokeAlpha = (byte)Math.Max(24, alpha - (i % 4) * 16);
                Color smoke = new Color((byte)18, (byte)13, (byte)8, smokeAlpha);

                Game1.spriteBatch.Draw(
                    _pixel,
                    new Rectangle(x - width / 2, y, width, height),
                    smoke
                );
            }

            for (int i = 0; i < 18; i++)
            {
                int along = 16 + (i * 53 + Wave(_animationSeconds * 19f, i * 17, 12)) % Math.Max(1, length - 32);
                int x = direction > 0 ? startX + along : startX - along;
                int y = centerY - beamHeight / 2 + (i * 11 + Wave(_animationSeconds * 21f, i * 13, 5)) % Math.Max(1, beamHeight);
                int shardLength = 4 + (i % 4) * 3;
                int shardDirection = direction * (i % 2 == 0 ? 1 : -1);
                Color dark = new Color((byte)42, (byte)28, (byte)14, (byte)(95 * intensity));
                Color bright = new Color((byte)255, (byte)199, (byte)42, (byte)(120 * intensity));

                DrawDebrisLine(x, y, x + shardDirection * shardLength, y - 2 - (i % 5), dark);

                if (i % 3 == 0)
                {
                    DrawDebrisLine(x, y, x + direction * (shardLength + 3), y + 1, bright);
                }
            }

            for (int i = 0; i < 12; i++)
            {
                int along = 20 + (i * 71) % Math.Max(1, length - 40);
                int x = direction > 0 ? left + along : left + length - along;
                int y = centerY + Wave(_animationSeconds * 15f, i * 9, beamHeight / 2 + 8);
                int size = 2 + i % 3;
                Color ember = new Color((byte)255, (byte)104, (byte)18, (byte)(95 * intensity));
                Game1.spriteBatch.Draw(_pixel, new Rectangle(x, y, size, size), ember);
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

            DrawOrb(startX, centerY, radius + 5, new Color((byte)210, (byte)112, (byte)10, (byte)(alpha * 0.34f)));
            DrawOrb(startX, centerY, radius + 2, new Color((byte)255, (byte)224, (byte)54, (byte)(alpha * 0.58f)));
            DrawOrb(startX, centerY, radius, new Color((byte)255, (byte)255, (byte)210, alpha));
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
