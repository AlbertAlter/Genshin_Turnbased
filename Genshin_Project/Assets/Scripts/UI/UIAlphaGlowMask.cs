using System;

/// <summary>Dense, zero-padded alpha filtering. Independent of Unity rendering and texture RGB.</summary>
public static class UIAlphaGlowMask
{
    public readonly struct Result
    {
        public readonly int Width, Height, Padding;
        public readonly byte[] Alpha;
        public Result(int width, int height, int padding, byte[] alpha)
        { Width = width; Height = height; Padding = padding; Alpha = alpha; }
    }

    public static Result Build(byte[] source, int width, int height, int size, int spreadPercent)
    {
        if (width <= 0 || height <= 0 || source == null || source.Length != checked(width * height))
            throw new ArgumentException("Alpha source dimensions do not match.");
        if (size < 0 || spreadPercent < 0 || spreadPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(size));
        int spread = (int)Math.Round(size * spreadPercent / 100.0);
        // Match the old filter's sigma, but integrate every texel instead of stamping 9x9 copies.
        int[] boxes = GaussianBoxes((size - spread) * .5);
        int padding = checked(spread + (boxes[0] + boxes[1] + boxes[2] - 3) / 2 + 2);
        int w = checked(width + 2 * padding), h = checked(height + 2 * padding);
        float[] pixels = new float[checked(w * h)], scratch = new float[pixels.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) pixels[(y + padding) * w + x + padding] = source[y * width + x] / 255f;
        if (spread > 0)
        {
            Dilate(pixels, scratch, w, h, spread, true);
            Dilate(scratch, pixels, w, h, spread, false);
        }
        foreach (int box in boxes)
        {
            int radius = (box - 1) / 2;
            if (radius == 0) continue;
            Blur(pixels, scratch, w, h, radius, true);
            Blur(scratch, pixels, w, h, radius, false);
        }
        byte[] alpha = new byte[pixels.Length];
        for (int i = 0; i < alpha.Length; i++)
            alpha[i] = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(pixels[i] * 255)));
        return new Result(w, h, padding, alpha);
    }

    private static int[] GaussianBoxes(double sigma)
    {
        if (sigma <= 0) return new[] { 1, 1, 1 };
        int low = Math.Max(1, (int)Math.Floor(Math.Sqrt(4 * sigma * sigma + 1)));
        if (low % 2 == 0) low--;
        int high = low + 2;
        int countLow = (int)Math.Round((12 * sigma * sigma - 3 * low * low - 12 * low - 9) / (-4.0 * low - 4));
        countLow = Math.Max(0, Math.Min(3, countLow));
        return new[] { countLow > 0 ? low : high, countLow > 1 ? low : high, countLow > 2 ? low : high };
    }

    private static void Blur(float[] source, float[] result, int width, int height, int radius, bool horizontal)
    {
        int length = horizontal ? width : height, lines = horizontal ? height : width;
        int stride = horizontal ? 1 : width;
        double divisor = radius * 2.0 + 1;
        for (int line = 0; line < lines; line++)
        {
            int start = horizontal ? line * width : line;
            double sum = 0;
            for (int i = 0; i <= radius && i < length; i++) sum += source[start + i * stride];
            for (int i = 0; i < length; i++)
            {
                result[start + i * stride] = (float)(sum / divisor);
                int remove = i - radius, add = i + radius + 1;
                if (remove >= 0) sum -= source[start + remove * stride];
                if (add < length) sum += source[start + add * stride];
            }
        }
    }

    private static void Dilate(float[] source, float[] result, int width, int height, int radius, bool horizontal)
    {
        int length = horizontal ? width : height, lines = horizontal ? height : width;
        int stride = horizontal ? 1 : width;
        int[] deque = new int[length];
        for (int line = 0; line < lines; line++)
        {
            int start = horizontal ? line * width : line, head = 0, tail = 0, next = 0;
            for (int i = 0; i < length; i++)
            {
                while (next < length && next <= i + radius)
                {
                    float value = source[start + next * stride];
                    while (tail > head && source[start + deque[tail - 1] * stride] <= value) tail--;
                    deque[tail++] = next++;
                }
                while (tail > head && deque[head] < i - radius) head++;
                result[start + i * stride] = tail > head ? source[start + deque[head] * stride] : 0;
            }
        }
    }
}
