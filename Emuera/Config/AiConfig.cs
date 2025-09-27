using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace MinorShift.Emuera
{
    sealed class AiConfig
    {
        public bool UseAi = false;
        public string Url;
        public string Token;
        public IDictionary<string, JToken> bodyMap;

        public void LoadAiConfig(string aiConfigTxtPath)
        {
            if (!File.Exists(aiConfigTxtPath))
            {
                // no ai config ,skip init
                return;
            }

            Dictionary<string, string> propMap = ReadProperties(aiConfigTxtPath);
            string usePlatform = "";
            Dictionary<string, Dictionary<string, string>> platPropMap =
                new Dictionary<string, Dictionary<string, string>>();
            foreach (KeyValuePair<string, string> kv in propMap)
            {
                if (kv.Key == "use_ai")
                {
                    this.UseAi = kv.Value == "1";
                    continue;
                }

                if (kv.Key == "use_platform")
                {
                    usePlatform = kv.Value;
                    continue;
                }

                if (kv.Key.StartsWith("url_"))
                {
                    string plat = kv.Key.Substring(4);
                    if (!platPropMap.ContainsKey(plat)) platPropMap[plat] = new Dictionary<string, string>();
                    platPropMap[plat]["url"] = kv.Value;
                    continue;
                }

                if (kv.Key.StartsWith("token_"))
                {
                    string plat = kv.Key.Substring(6);
                    if (!platPropMap.ContainsKey(plat)) platPropMap[plat] = new Dictionary<string, string>();
                    platPropMap[plat]["token"] = kv.Value;
                    continue;
                }

                if (kv.Key.Contains("_"))
                {
                    int underscoreIndex = kv.Key.LastIndexOf('_');
                    if (underscoreIndex <= 0 || underscoreIndex >= kv.Key.Length - 1)
                    {
                        continue;
                    }

                    string propKey = kv.Key.Substring(0, underscoreIndex);
                    string plat = kv.Key.Substring(underscoreIndex + 1);
                    platPropMap[plat][propKey] = kv.Value;
                }
            }

            if (!UseAi)
            {
                return;
            }

            if (string.IsNullOrEmpty(usePlatform))
            {
                return;
            }

            if (!platPropMap.ContainsKey(usePlatform))
            {
                return;
            }

            this.bodyMap = new Dictionary<string, JToken>();
            foreach (KeyValuePair<string, string> kv in platPropMap[usePlatform])
            {
                if (kv.Key == "url")
                {
                    this.Url = kv.Value;
                    continue;
                }

                if (kv.Key == "token")
                {
                    this.Token = kv.Value;
                    continue;
                }

                NumberType type = NumberChecker.GetNumberType(kv.Value);
                switch (type)
                {
                    case NumberType.None:
                        this.bodyMap[kv.Key] = kv.Value;
                        break;
                    case NumberType.Double:
                        this.bodyMap[kv.Key] = double.Parse(kv.Value);
                        break;
                    case NumberType.Float:
                        this.bodyMap[kv.Key] = float.Parse(kv.Value);
                        break;
                    case NumberType.Integer:
                        this.bodyMap[kv.Key] = long.Parse(kv.Value);
                        break;
                }
            }
        }

        public enum NumberType
        {
            None, // 不是数字
            Integer, // 整数
            Float, // 单精度浮点数
            Double // 双精度浮点数
        }

        public static class NumberChecker
        {
            public static NumberType GetNumberType(string input)
            {
                // 处理空值或空白字符串
                if (string.IsNullOrWhiteSpace(input))
                    return NumberType.None;

                // 先尝试判断是否为整数
                if (long.TryParse(input, out _))
                    return NumberType.Integer;

                // 检查是否包含小数点或指数符号
                bool hasDecimalPoint = input.Contains(".");
                bool hasExponent = input.IndexOfAny(new[] { 'e', 'E' }) != -1;

                // 尝试解析为单精度浮点数
                if (float.TryParse(input, out float floatValue))
                {
                    // 检查是否在float范围内但超出int范围
                    if (floatValue > int.MaxValue || floatValue < int.MinValue)
                        return NumberType.Float;

                    // 检查是否有小数部分或指数表示
                    if (hasDecimalPoint || hasExponent)
                        return NumberType.Float;
                }

                // 尝试解析为双精度浮点数
                if (double.TryParse(input, out double doubleValue))
                {
                    // 检查是否在double范围内但超出float范围
                    if (doubleValue > float.MaxValue || doubleValue < float.MinValue)
                        return NumberType.Double;

                    // 检查是否有小数部分或指数表示
                    if (hasDecimalPoint || hasExponent)
                        return NumberType.Double;
                }

                // 都不是
                return NumberType.None;
            }
        }

        /// <summary>
        /// 读取属性文件中的所有键值对
        /// </summary>
        /// <param name="filePath">属性文件的路径</param>
        /// <returns>包含所有属性及其值的字典</returns>
        /// <exception cref="FileNotFoundException">当指定文件不存在时抛出</exception>
        private Dictionary<string, string> ReadProperties(string filePath)
        {
            // 初始化存储属性的字典
            Dictionary<string, string> properties = new Dictionary<string, string>();

            // 检查文件是否存在
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("属性文件不存在", filePath);
            }

            // 读取文件的所有行
            string[] lines = File.ReadAllLines(filePath);

            // 处理每一行
            foreach (string line in lines)
            {
                // 跳过空行和注释行(假设以#或//开头的是注释)
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) ||
                    trimmedLine.StartsWith("#") ||
                    trimmedLine.StartsWith("//"))
                {
                    continue;
                }

                // 查找第一个等号的位置
                int equalsIndex = trimmedLine.IndexOf('=');
                if (equalsIndex <= 0) // 等号不能是第一个字符
                {
                    continue; // 跳过格式不正确的行
                }

                // 分割键和值
                string key = trimmedLine.Substring(0, equalsIndex).Trim();
                string value = trimmedLine.Substring(equalsIndex + 1).Trim();

                // 添加到字典中，如果键已存在则覆盖
                if (properties.ContainsKey(key))
                {
                    properties[key] = value;
                }
                else
                {
                    properties.Add(key, value);
                }
            }

            return properties;
        }
    }
}