# BiliCommunityGuard

B 站社区守护工具 —— 自动扫描指定 UP 主的视频和动态评论区，对黑名单用户的评论进行举报。

## 功能

- 监控多个 UP 主的最新视频和动态评论区
- 支持多账号轮转举报，账号触发频控后自动冷却
- 举报历史持久化，避免短时间内重复举报同一评论
- 调试模式（`dry_run`）：只扫描记录，不发送真实举报请求
- 配置文件支持中英文双语键名

## 运行环境

- Windows 10/11
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（使用自包含发布版则无需安装）

## 快速开始

1. 下载并解压发布包，或自行编译（见下方）
2. 运行 `BiliCommunityGuard.exe`
3. 点击「生成示例文件」，程序会在 exe 同目录下创建 `config.yaml`、`cookie.json`、`blacklist.txt`
4. 按照下方说明编辑三个配置文件
5. 点击「重新加载」，确认账号和黑名单已正确读取
6. 点击「启动」

## 配置文件

### config.yaml

所有配置项支持中文键名和英文键名，示例：

```yaml
扫描间隔秒数: 60

保护UP主UID列表:
  - 12345678
  - 87654321

最新视频保护数量: 10
最新动态保护数量: 10
每轮扫描内容数: 1
评论页大小: 20

举报设置:
  原因代码: 1   # 1=垃圾广告 4=引战 7=人身攻击
  补充说明: ""

仅调试不真实举报: false
再次举报间隔秒数: 600
账号冷却秒数: 300

请求间隔毫秒:
  最小值: 1500
  最大值: 4000
```

### cookie.json

数组格式，每个元素为一个账号的 Cookie 字符串（从浏览器开发者工具中复制）：

```json
[
  "SESSDATA=xxx; bili_jct=xxx; DedeUserID=111111; DedeUserID__ckMd5=xxx; sid=xxx",
  "SESSDATA=yyy; bili_jct=yyy; DedeUserID=222222; DedeUserID__ckMd5=yyy; sid=yyy"
]
```

必要字段：`SESSDATA`、`bili_jct`、`DedeUserID`。

### blacklist.txt

每行一个 UID（数字），`#` 开头为注释：

```
# 黑名单 UID 列表
123456
987654
```

## 编译

```bash
# 普通构建
dotnet build -c Release

# 发布为 win-x64 单文件自包含 exe
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 运行时数据

以下文件由程序自动创建和维护，位于 exe 同目录下：

| 文件 | 说明 |
|---|---|
| `data/state.json` | 扫描游标与内容访问记录 |
| `data/report-history.json` | 各账号举报历史，用于冷却判断 |
