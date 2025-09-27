# EmuerAI
Emuera with AI funcs


# 使用方法
## AI配置

- 在游戏文件- Emuera.exe 同级目录创建 ai_config.txt , 以下是参考
```properties
use_ai=1
use_platform=dzmm
url_dzmm=https://www.gpt4novel.com/api/xiaoshuoai/ext/v1/chat/completions
token_dzmm=your_token
model_dzmm=nalang-xl-10
stream_dzmm=stream
temperature_dzmm=0.7
max_tokens_dzmm=800
top_p_dzmm=0.35
repetition_penalty_dzmm=1.05
```
## AI使用
- 在脚本文件中调用内建函数

| 函数名   | 是否流式输出 | 是否等待 |
|----------|--------------|----------|
| AITALK   | 否           | 否       |
| AITALKW  | 否           | 是       |
| AITALKS  | 是           | 否       |
| AITALKWS | 是           | 是       |

    - **流式输出**：大模型的响应是流式的，为了防止长时间等待的不好体验，支持实时打印输出。
    - **等待**：AI 回复完后，需要按任意键才能继续。
- 示例
```Emuera
 AITALKS "你是秘法店的店长，掌管很多秘法", "仔细盯着你看"
```
