using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrpWin
{
    /// <summary>
    /// 极简 JSON 解析器。
    ///
    /// 为什么要自己写：FrpWin 是 net48 的单文件程序，为了读取 frps 的 Dashboard
    /// 管理接口（/api/clients、/api/proxy/tcp ...），只需要“把 JSON 变成字典和列表”
    /// 这一点能力。为此引入 Newtonsoft.Json 或 System.Web.Extensions 都会让
    /// 程序多一个外部依赖或整块程序集，得不偿失。这里 200 行搞定。
    ///
    /// 解析结果映射：
    ///   对象   -&gt; Dictionary&lt;string, object&gt;
    ///   数组   -&gt; List&lt;object&gt;
    ///   字符串 -&gt; string
    ///   数字   -&gt; double
    ///   布尔   -&gt; bool
    ///   null   -&gt; null
    /// </summary>
    internal static class Json
    {
        // ------------------------------------------------------------------
        //  对外读取辅助
        // ------------------------------------------------------------------

        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON 内容为空");
            int i = 0;
            object value = ParseValue(text, ref i);
            SkipWhitespace(text, ref i);
            if (i < text.Length)
                throw new FormatException(Loc.F("JSON 末尾有多余内容（位置 {0}）", i));
            return value;
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        /// <summary>当 value 本身就是数组时，按数组取；否则当成对象的 key 取。</summary>
        public static object Get(object obj, string key)
        {
            var dict = obj as Dictionary<string, object>;
            if (dict == null) return null;
            object v;
            return dict.TryGetValue(key, out v) ? v : null;
        }

        public static string AsString(object value)
        {
            if (value == null) return "";
            var s = value as string;
            if (s != null) return s;
            if (value is bool) return ((bool)value) ? "true" : "false";
            if (value is double) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            return value.ToString();
        }

        public static string Str(object obj, string key, string fallback)
        {
            object v = Get(obj, key);
            return v == null ? fallback : AsString(v);
        }

        public static int Int(object obj, string key, int fallback)
        {
            object v = Get(obj, key);
            if (v is double) return (int)Math.Round((double)v, MidpointRounding.AwayFromZero);
            if (v is string)
            {
                double d;
                if (double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    return (int)Math.Round(d, MidpointRounding.AwayFromZero);
            }
            return fallback;
        }

        public static bool Bool(object obj, string key, bool fallback)
        {
            object v = Get(obj, key);
            if (v is bool) return (bool)v;
            if (v is string) return string.Equals((string)v, "true", StringComparison.OrdinalIgnoreCase);
            if (v is double) return (double)v != 0;
            return fallback;
        }

        public static string[] StrArray(object obj, string key)
        {
            var arr = AsArray(Get(obj, key));
            if (arr == null) return new string[0];
            var list = new List<string>(arr.Count);
            foreach (object item in arr) list.Add(AsString(item));
            return list.ToArray();
        }

        // ------------------------------------------------------------------
        //  递归下降解析
        // ------------------------------------------------------------------

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 内容不完整");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException(Loc.F("无法识别的 JSON 字面量（位置 {0}）", i));
            i += word.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            i++;                                    // 跳过 '{'
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == '}') { i++; return dict; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException(Loc.F("JSON 对象的键必须是字符串（位置 {0}）", i));
                string key = ParseString(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException(Loc.F("JSON 对象的键后面缺少 ':'（位置 {0}）", i));
                i++;

                dict[key] = ParseValue(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 对象没有闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return dict; }
                throw new FormatException(Loc.F("JSON 对象里出现了意外字符 '{0}'（位置 {1}）", s[i], i));
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++;                                    // 跳过 '['
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == ']') { i++; return list; }

            while (true)
            {
                list.Add(ParseValue(s, ref i));

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 数组没有闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException(Loc.F("JSON 数组里出现了意外字符 '{0}'（位置 {1}）", s[i], i));
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++;                                    // 跳过开头的 '"'
            var sb = new System.Text.StringBuilder();

            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON 字符串没有闭合");
                char c = s[i++];

                if (c == '"') return sb.ToString();

                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length) throw new FormatException("JSON 转义序列不完整");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("\\u 转义不完整");
                        int code = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int digit = HexValue(s[i + k]);
                            if (digit < 0) throw new FormatException("\\u 转义里有非十六进制字符");
                            code = (code << 4) | digit;
                        }
                        i += 4;
                        sb.Append((char)code);
                        break;
                    default:
                        throw new FormatException(Loc.F("不支持的 JSON 转义 '\\{0}'", e));
                }
            }
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') { i++; continue; }
                break;
            }
            if (i == start) throw new FormatException(Loc.F("无法识别的 JSON 值（位置 {0}）", start));

            double d;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException(Loc.F("无法解析的 JSON 数字：“{0}”", s.Substring(start, i - start)));
            return d;
        }
    }
}
