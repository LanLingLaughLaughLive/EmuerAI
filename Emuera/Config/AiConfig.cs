using System.Collections.Generic;
using System.IO;

namespace MinorShift.Emuera
{
    sealed class AiConfig
    {
        public bool UseAi = false;
        public string Url;
        public string Token;
        
        public void LoadAiConfig(string aiConfigTxtPath)
        {
            if (!File.Exists(aiConfigTxtPath))
            {
                // no ai config ,skip init
                return;
            }
            Dictionary<string, string> propMap = ReadProperties(aiConfigTxtPath);
            string usePlatform = "";
            Dictionary<string, Dictionary<string, string>> platPropMap = new Dictionary<string, Dictionary<string, string>>(); 
            foreach (KeyValuePair<string,string> kv in propMap)
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

            Dictionary<string, string> props = platPropMap[usePlatform];
            this.Url = props["url"];
            this.Token = props["token"];
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