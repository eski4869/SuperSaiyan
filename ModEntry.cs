using System;
using System.IO;
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
        [OnLevelStart]
        public static void OnLevelStart()
        {
            SuperSaiyanAura.EnsureAdded();
        }
    }

    public sealed class SuperSaiyanAura : Entity, JumpKing.Util.IDrawable
    {
        private const float AuraDurationSeconds = 20f;
        private const float StatePollIntervalSeconds = 1f;
        private const string StateFilePath = @"C:\ChannelPoint\super_saiyan.state";

        private static SuperSaiyanAura _instance;

        private Texture2D[] _auraFrames;
        private Texture2D _pixel;
        private KeyboardState _previousKeyboardState;
        private DateTime _lastStateWriteUtc = DateTime.MinValue;
        private float _remainingSeconds;
        private float _pollSeconds;
        private float _animationSeconds;

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
            LoadStateTimestamp();
            CreateTextures();
        }

        protected override void Update(float delta)
        {
            _animationSeconds += delta;
            _pollSeconds += delta;

            KeyboardState keyboardState = Keyboard.GetState();
            bool shiftDown =
                keyboardState.IsKeyDown(Keys.LeftShift) ||
                keyboardState.IsKeyDown(Keys.RightShift);

            if (shiftDown && WasKeyPressed(keyboardState, Keys.S))
            {
                Activate();
            }

            _previousKeyboardState = keyboardState;

            if (_remainingSeconds <= 0f && _pollSeconds >= StatePollIntervalSeconds)
            {
                _pollSeconds = 0f;
                PollStateFile();
            }

            if (_remainingSeconds > 0f)
            {
                _remainingSeconds = Math.Max(0f, _remainingSeconds - delta);
            }
        }

        public override void Draw()
        {
            if (_remainingSeconds <= 0f || _pixel == null)
            {
                return;
            }

            PlayerEntity player = EntityManager.instance.Find<PlayerEntity>();

            if (player == null || Game1.instance == null)
            {
                return;
            }

            Rectangle hitbox = Camera.TransformRect(player.m_body.GetHitbox());
            DrawEnergyAura(hitbox);
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

        private void Activate()
        {
            _remainingSeconds = AuraDurationSeconds;
        }

        private void LoadStateTimestamp()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    _lastStateWriteUtc = File.GetLastWriteTimeUtc(StateFilePath);
                }
            }
            catch
            {
            }
        }

        private void PollStateFile()
        {
            try
            {
                if (!File.Exists(StateFilePath))
                {
                    return;
                }

                DateTime writeUtc = File.GetLastWriteTimeUtc(StateFilePath);

                if (writeUtc <= _lastStateWriteUtc)
                {
                    return;
                }

                _lastStateWriteUtc = writeUtc;
                Activate();
            }
            catch
            {
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
}


































