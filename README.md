# Desktop Pet · 自定义桌宠

基于 C#、Windows Forms 和 .NET Framework 的 Windows 桌面宠物。支持自定义角色图片、人设、互动台词，以及兼容 OpenAI Chat Completions 协议的 AI 聊天接口。

## 功能

- 启动器管理角色，支持新建、编辑和切换皮肤。
- 透明桌宠窗口，支持拖动、摸头、跳跃、散步和安静陪伴。
- 为表情和动作配置独立图片，为每个角色设置人设和默认台词。
- 可选 AI 聊天、按角色保存聊天记录、调整聊天上下文。
- 生成并缓存日常短句；没有 API 配置也能使用离线桌宠。

## 从源码运行

需要 Windows 和 .NET Framework 4.x 编译器。构建脚本直接使用系统中的 `csc.exe`；不需要 Node.js 或 Python。建议在 Windows 10/11、.NET Framework 4.8 或更高的 4.x 环境验证运行。

1. 下载或克隆整个仓库。
2. 在项目目录打开 PowerShell，执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

3. 双击生成的 `DesktopPet.exe`。
4. 首次运行点击“新建角色”，导入自己的图片并填写人设，再点击“启动桌宠”。需要 AI 功能时，在“设置 API / 模型”中填写自己的配置。

程序会读取旁边的 `characters/` 角色资源，不能只移动 EXE。角色、配置和聊天记录会写入程序目录，请将项目放在当前用户可写的文件夹。

本仓库只提供源码、文档和测试，不附带作者的宠物图片、个人角色设定、编译后的 EXE 或安装包。`characters/` 和 `runner.json` 由你创建角色、启动桌宠时生成，并被 Git 忽略。

## 文档

- [使用说明](使用说明.md)：启动流程、角色编辑、AI 配置和聊天记录。
- [角色包说明](角色包说明.md)：角色目录、素材和配置格式。

## 测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-chat-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-smoke.ps1
```

测试脚本会自动生成几何图形测试图片，编译程序并在 `tests/results/` 生成结果；不需要作者的宠物素材。`run-persona-live.ps1` 是单独的真实 API 测试，执行前应先阅读脚本和费用相关配置。

## 配置与隐私

- `.env.example` 仅提供空白配置模板。
- `.env`、`model-settings.json`、`user-data/`、日志和测试输出不会提交到 Git。
- API 密钥使用 Windows 当前用户保护功能加密，换电脑后需要重新填写。
- 分享程序时，只提供程序、选定角色资源和说明，不附带自己的 API 配置或聊天记录。
- 自定义角色可能包含个人设定或第三方素材，进一步分享时请自行检查内容和使用授权。
