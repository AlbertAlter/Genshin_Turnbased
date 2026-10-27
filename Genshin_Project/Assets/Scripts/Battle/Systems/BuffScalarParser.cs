using System;
using System.Globalization;

/// <summary>只读标量语法：四则、括号、min(a,b)、Index、Count及调用者明确提供的属性。不执行脚本。</summary>
public static class BuffScalarParser
{
    public static float Evaluate(string expression, Func<int, float> readIndex = null,
        Func<string, float> readCount = null, Func<string, float> readAttribute = null)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 1024)
            throw new FormatException("Buff 数值表达式为空或过长。");
        var parser = new Parser(expression, readIndex, readCount, readAttribute);
        double value = parser.Sum();
        parser.Space();
        if (parser.Position != expression.Length || double.IsNaN(value)
            || double.IsInfinity(value) || value > float.MaxValue || value < -float.MaxValue)
            throw new FormatException("无效的 Buff 数值表达式：" + expression);
        return (float)value;
    }

    private sealed class Parser
    {
        private readonly string text;
        private readonly Func<int, float> readIndex;
        private readonly Func<string, float> readCount;
        private readonly Func<string, float> readAttribute;
        private int depth;
        public int Position;

        public Parser(string text, Func<int, float> readIndex, Func<string, float> readCount,
            Func<string, float> readAttribute)
        { this.text = text; this.readIndex = readIndex; this.readCount = readCount; this.readAttribute = readAttribute; }

        public void Space()
        { while (Position < text.Length && char.IsWhiteSpace(text[Position])) Position++; }

        private bool Take(char c)
        {
            Space();
            if (Position >= text.Length || text[Position] != c) return false;
            Position++;
            return true;
        }

        private void Need(char c)
        { if (!Take(c)) throw new FormatException("Buff 表达式缺少 " + c + "：" + text); }

        public double Sum()
        {
            double value = Product();
            while (true)
            {
                if (Take('+')) value += Product();
                else if (Take('-')) value -= Product();
                else return value;
            }
        }

        private double Product()
        {
            double value = Atom();
            while (true)
            {
                if (Take('*')) value *= Atom();
                else if (Take('/'))
                {
                    double divisor = Atom();
                    if (divisor == 0d) throw new FormatException("Buff 表达式除数不能为0。");
                    value /= divisor;
                }
                else return value;
            }
        }

        private double Atom()
        {
            if (++depth > 32) throw new FormatException("Buff 表达式嵌套过深。");
            try
            {
                if (Take('+')) return Atom();
                if (Take('-')) return -Atom();
                if (Take('(')) { double value = Sum(); Need(')'); return value; }
                Space();
                if (Position < text.Length && char.IsLetter(text[Position]))
                {
                    string function = Name();
                    Space();
                    if (Position >= text.Length || text[Position] != '(')
                    {
                        if (readAttribute == null) throw new FormatException("缺少 Buff 属性读取上下文：" + function);
                        return Finite(readAttribute(function));
                    }
                    Need('(');
                    if (string.Equals(function, "min", StringComparison.OrdinalIgnoreCase))
                    {
                        double first = Sum();
                        Need(',');
                        double second = Sum();
                        Need(')');
                        return Math.Min(first, second);
                    }
                    if (function == "Count")
                    {
                        string id = Name();
                        if (id == "Self") { Need(','); id = Name(); }
                        Need(')');
                        if (readCount == null) throw new FormatException("缺少 Count 读取上下文。");
                        return Finite(readCount(id));
                    }
                    if (function == "Index")
                    {
                        string index = Name();
                        Need(')');
                        if (!int.TryParse(index, out int number) || number < 1 || readIndex == null)
                            throw new FormatException("Index 需要有效的武器参数序号及上下文。");
                        return Finite(readIndex(number));
                    }
                    throw new FormatException("未支持的 Buff 表达式函数：" + function);
                }
                int start = Position;
                while (Position < text.Length && (char.IsDigit(text[Position]) || text[Position] == '.')) Position++;
                if (Position < text.Length && (text[Position] == 'e' || text[Position] == 'E'))
                {
                    Position++;
                    if (Position < text.Length && (text[Position] == '+' || text[Position] == '-')) Position++;
                    while (Position < text.Length && char.IsDigit(text[Position])) Position++;
                }
                if (start == Position || !double.TryParse(text.Substring(start, Position - start),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double scalar)
                    || double.IsNaN(scalar) || double.IsInfinity(scalar))
                    throw new FormatException("Buff 表达式需要数字：" + text);
                return scalar;
            }
            finally { depth--; }
        }

        private string Name()
        {
            Space();
            int start = Position;
            while (Position < text.Length && (char.IsLetterOrDigit(text[Position]) || text[Position] == '_')) Position++;
            if (Position == start) throw new FormatException("Buff 表达式缺少名称。");
            return text.Substring(start, Position - start);
        }

        private static float Finite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new FormatException("Buff 引用的数值不是有限数。");
            return value;
        }
    }
}
