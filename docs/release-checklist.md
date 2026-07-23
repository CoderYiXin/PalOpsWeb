# 发布检查清单

> 语言：**简体中文** | [English](release-checklist.en.md)

本清单用于 PalOps Web **1.3.1** 的 GitHub 源码与 Windows `win-x64` 发布。

## 1. 源码仓库

- [ ] 根目录 `README.md` 为中文项目首页，`README.en.md` 为对应英文项目首页；
- [ ] 中文 README 只引用 `docs/images/zh-CN/`，英文 README 只引用 `docs/images/en-US/`；
- [ ] 两种语言各有 37 张、共 74 张 **1920×1080 WebP** 产品截图，全部来自当前 Vue 页面并使用伪数据；
- [ ] `docs/*.md` 默认中文，并存在对应 `.en.md`；
- [ ] 不包含历史设计稿、内部实施计划、单次缺陷报告、打包审计或临时截图脚本；
- [ ] 不包含 `node_modules`、`bin`、`obj`、`dist`、`.git`、缓存、运行数据、日志或密钥；
- [ ] 版本元数据、README、发布命令与标签均为 `1.3.1`；
- [ ] `.github/workflows/build.yml` 可通过 GitHub Actions。

## 2. 标准构建

```powershell
.\scripts\build.ps1
```

必须完成：

1. 锁文件安装；
2. 前端功能与发布契约；
3. TypeScript 类型检查；
4. Vite 生产构建；
5. npm 高危依赖审计；
6. .NET 10 restore/build；
7. 目录、地图、文档和发布树校验。

## 3. 运行烟雾测试

| 范围 | 验收结果 |
|---|---|
| 启动 | 控制台显示版本、环境、数据目录、存档目录和监听地址，不泄露密钥 |
| 权限 | Owner、Administrator、Operator、Auditor、Viewer 权限与导航一致，后端策略不可绕过 |
| 概览/统计 | 进程、主机、在线人数、FPS、版本、存档与历史趋势可正常展示 |
| 玩家/公会 | 在线与存档玩家、公会成员、据点归属证据和地图跳转可用 |
| 地图 | Palpagos / World Tree 离线底图、94 个快速传送、实时玩家和据点正常渲染 |
| 地图刷新 | 玩家刷新支持 1/2/3/5/10/15/30 秒且默认 3 秒；据点/自定义标记独立 30 秒刷新 |
| 配置中心 | 结构化配置、原文编辑、校验、差异、备份、原子保存和安全重启可用 |
| 维护/崩溃守护 | 维护编排、自动恢复、健康验证和熔断状态正确 |
| 正常停止 | `Shutdown 1` 成功，显示 10→1 秒倒计时，残留 PID 被验证后清理，最终为 Stopped |
| 强制停止 | 仅 Owner，可审计，进程身份重新验证 |
| RCON | 高危命令要求风险确认，命令格式与返回历史正确 |
| 存档 | 解析、备份、恢复预检和差异比较为受保护流程，失败不覆盖最后成功索引 |
| 玩家纪律 | 白名单、封禁、身份、违规与踢出记录完整；UserId 校验阻止误写 |
| 插件与模组 | 清单、版本、依赖、兼容性、备份和回滚信息可用 |
| 通知 | 渠道测试、模板、重试和历史正常，机密脱敏 |
| 主题与尺寸 | 浅色默认、深色持久化；1366×768 与 1920×1080 可用 |

## 4. 地图专项

- [ ] Palpagos / World Tree 配置和瓦片齐全；
- [ ] 三种语言 POI 均为 1,251 条，30 个固定分类非空；
- [ ] 快速传送内置 94 条，并明确显示相对已知 149 条上游记录的覆盖差距；
- [ ] 默认只启用服务器数据、快速传送和高塔；
- [ ] 玩家标记位于固定 POI 上层，同坐标快速传送仍可识别；
- [ ] 限速底图时服务器实体和默认 POI 仍能在骨架就绪后渲染；
- [ ] 后端不枚举、哈希或健康检查栅格瓦片。

## 5. PalDefender 专项

- [ ] 安装目录和 DLL 正确；
- [ ] REST Token 使用实际 Token，不使用 `TokenExample.json`；
- [ ] 白名单优先使用 `whitelist_add` / `whitelist_remove` 并验证 `WhiteList.json`；
- [ ] UserId 严格校验，玩家名和存档 PlayerUID 不会误写到白名单；
- [ ] 配置路径白名单、JSON 类型、大小、穿越和重解析点校验有效；
- [ ] SHA-256 冲突阻止覆盖外部修改；
- [ ] 保存产生备份并可重新解析；
- [ ] 审计不包含配置正文和机密。

## 6. 严格发布

```powershell
.\scripts\fetch-map-tiles.ps1 -Layer all
.\scripts\publish-win-x64.ps1 -Version 1.3.1
```

检查：

- [ ] `artifacts/palops-web-1.3.1-win-x64/`、ZIP 与 `.sha256` 同时生成；
- [ ] 解压后可由 `start.cmd` 启动；
- [ ] `wwwroot` 为当前构建；
- [ ] 发布树不含 Python、源码缓存、运行数据和密钥；
- [ ] README、许可证、第三方声明和必要部署文档完整。

## 7. GitHub Release

- [ ] 创建 `v1.3.1` 标签；
- [ ] 上传 ZIP 和 SHA-256；
- [ ] 发布说明引用 `CHANGELOG.md` 的 1.3.1 条目；
- [ ] 明确生命周期控制仅支持 Windows 本机；
- [ ] 提醒升级前备份存档、PalOps 数据和 PalDefender 配置。

PalDefender 发布前的详细检查依据 [PalDefender 部署指南](paldefender-deployment.md)。
