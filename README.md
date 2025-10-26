# DCS Miz Translate MCP

一个用于翻译 DCS (Digital Combat Simulator) .miz 任务文件的 Model Context Protocol (MCP) 服务器。

## 🚀 功能特性

- **自动翻译 DCS 任务文件**: 支持将 .miz 文件中的文本内容翻译为目标语言
- **智能缓存系统**: 避免重复翻译相同内容，提高效率
- **多语言支持**: 支持翻译为多种语言（如中文 CN）
- **MCP 协议支持**: 基于 Model Context Protocol，可与支持 MCP 的 AI 客户端集成
- **专有名词保护**: 自动识别并保护飞机呼号等专有名词不被翻译
- **Lua 脚本翻译支持**: 支持翻译任务附带的 Lua 脚本（需手动替换到 DEFAULT 目录）

## 📋 系统要求

- .NET 9.0 或更高版本
- Windows、macOS 或 Linux
- 支持 MCP 的 AI 客户端（如 Claude Desktop、VS Code Copilot 等）

## 🛠️ 安装与配置

### 1. 配置 MCP 客户端

在您的 MCP 客户端配置文件中添加以下配置：

#### VS Code (.vscode/mcp.json)
```json
{
  "servers": {
    "dcs-miz-translate": {
      "type": "stdio",
      "command": "dnx",
      "args": ["DcsMizTranslate@0.5.0-beta", "--yes"]
    }
  },
  "inputs": []
}
```

#### Claude Desktop
```json
{
  "mcpServers": {
    "dcs-miz-translate": {
      "type": "stdio",
      "command": "dnx",
      "args": ["DcsMizTranslate@0.5.0-beta", "--yes"]
    }
  }
}
```

## 🎯 使用方法

### 基本用法

在支持 MCP 的 AI 客户端中使用以下命令：

```
#translate_miz_file  翻译 #file:任务文件名.miz 为中文
```

### 示例

```
#translate_miz_file 翻译 #file:Dynamic mission 1400.miz 为中文
```

### 支持的语言代码

- `CN` - 中文（简体）
- 可根据需要扩展其他语言代码

### Lua 脚本翻译

1. 当工具完成后,会在 miz 文件中的本地化目录添加翻译后的lua文件
2. 先备份你 miz 文件中的 DEFAULT 目录下的lua
3. 将本地化目录的 lua 复制到DEFAULT目录并替换

## 📁 项目结构

```
dcs-miz-translate-mcp/
├── .vscode/
│   └── mcp.json              # VS Code MCP 配置
├── src/
│   └── DcsMizTranslate/
│       ├── DcsMizTranslate.csproj
│       ├── Program.cs        # 应用程序入口点
│       └── Tools/
│           └── DcsMizTranslateTools.cs  # 核心翻译工具
├── artifacts/                # 翻译结果输出目录
└── README.md
```
## 从源代码生成

### 1. 克隆项目

```bash
git clone https://github.com/wu-yafeng/dcs-miz-translate-mcp.git
cd dcs-miz-translate-mcp
```

### 2. 本地调试

[README](src/DcsMizTranslate/README.md)

## 🔧 工作原理

1. **解压缩 .miz 文件**: .miz 文件本质上是包含任务数据的 ZIP 压缩包
2. **提取本地化资源**: 从 `l10n/DEFAULT/dictionary` 文件中提取可翻译文本
3. **智能翻译**: 使用 AI 服务翻译文本，同时保护专有名词
4. **缓存优化**: 将翻译结果缓存到本地，避免重复翻译
5. **写入翻译结果**: 将翻译后的内容写入 .miz 文件的对应语言目录

## 📝 技术栈

- **.NET 9.0**: 主要开发框架
- **Model Context Protocol (MCP)**: 协议支持
- **NLua**: Lua 脚本解析（用于处理 DCS 字典文件）
- **System.IO.Compression**: ZIP 文件处理
- **Microsoft.Extensions.AI**: AI 服务集成

## 🔍 翻译规则

- 不翻译专有名词（如飞机呼号）
- 过滤短文本（长度 < 15 字符）
- 跳过特定前缀的条目（如 `DictKey_ActionRadioText`）
- 保持原始格式和结构

## 📊 缓存系统

翻译结果缓存在以下位置：
- Windows: `%USERPROFILE%\Documents\DCSMizTranslate\{语言代码}\cache.json`
- macOS/Linux: `~/Documents/DCSMizTranslate/{语言代码}/cache.json`

## 🤝 贡献指南

1. Fork 本项目
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

## 📄 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 🆘 问题排查

### 常见问题

1. **翻译失败**: 检查 .miz 文件格式是否正确
2. **缓存问题**: 删除缓存文件后重试
3. **权限问题**: 确保对文档目录有写入权限

### 获取帮助

- 提交 [Issue](https://github.com/wu-yafeng/dcs-miz-translate-mcp/issues)

**注意**: 本工具仅用于学习和研究目的。请确保遵守 DCS 的使用条款和版权规定。
