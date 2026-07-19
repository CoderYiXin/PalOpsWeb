# 发布检查清单

> 语言：**简体中文** | [English](release-checklist.en.md)

## 1. 源码仓库

- [ ] 根目录默认 `README.md` 为中文，`README.en.md` 为对应英文说明；
- [ ] `docs/*.md` 默认中文，并存在对应 `.en.md`；
- [ ] 不包含历史设计稿、内部实施计划、单次缺陷报告或验证报告；
- [ ] 不包含 `node_modules`、`bin`、`obj`、`.git`、缓存、运行数据或密钥；
- [ ] `SOURCE-SHA256SUMS.txt` 与源码一致；
- [ ] `.github/workflows/build.yml` 可通过 GitHub Actions。

## 2. 标准构建

```powershell
.\scripts\build.ps1
```

必须完成：

1. 锁文件安装；
2. 前端契约；
3. TypeScript；
4. Vite 构建；
5. npm 高危审计；
6. .NET restore/build；
7. 目录、地图、文档和源码/发布树校验。

## 3. 运行烟雾测试

| 范围 | 验收结果 |
|---|---|
| 启动 | 控制台显示版本、环境、数据目录、存档目录和监听地址，不泄露密钥 |
| 权限 | 五类角色权限与导航一致，后端策略不可绕过 |
| 存档 | 成功索引可用，失败解析不覆盖旧索引，无人在线时不频繁解析 |
| 启动 | 重复请求不会创建第二个受管 PalServer |
| 正常停止 | `Shutdown 1` 成功，页面显示 10→1 秒倒计时，残留 PID 自动清理，最终为 Stopped |
| 强制停止 | 仅 Owner，可审计，进程身份重新验证 |
| RCON | 高危命令只需勾选风险确认，不再输入 `CONFIRM` |
| 地图 | 首次进入有加载进度；服务器数据、快速传送和高塔不等待后续刷新 |
| 公会据点 | 已定位据点可立即跳转，未归属据点不会丢失 |
| PalDefender | REST、版本、Token、配置读取、校验、备份、保存和 reload 提示正常 |
| 通知 | 渠道测试、模板、重试和历史正常，机密脱敏 |
| 日志 | 成功 HTTP 生命周期不刷屏，异常仍可诊断 |
| 主题与尺寸 | 浅色默认、深色持久化；1366×768 与 1920×1080 可用 |

## 4. 地图专项

- [ ] Palpagos / World Tree 配置和瓦片齐全；
- [ ] 三种语言 POI 均为 1,242 条；
- [ ] 30 个分类非空；
- [ ] 默认只启用服务器数据、快速传送和高塔；
- [ ] 限速底图时服务器实体和默认 POI 仍能在骨架就绪后渲染；
- [ ] 从公会据点跳转先使用已知坐标定位；
- [ ] 后端不枚举、哈希或健康检查栅格瓦片。

## 5. PalDefender 专项

- [ ] 安装目录和 DLL 正确；
- [ ] REST Token 使用实际 Token，不使用 `TokenExample.json`；
- [ ] 401/403/端口冲突和防火墙排查已验证；
- [ ] 配置路径白名单、JSON 类型、大小、穿越和重解析点校验有效；
- [ ] SHA-256 冲突阻止覆盖外部修改；
- [ ] 保存产生备份并可重新解析；
- [ ] 审计不包含配置正文和机密。

## 6. 严格发布

```powershell
.\scripts\fetch-map-tiles.ps1 -Layer all
.\scripts\publish-win-x64.ps1 -Version 1.1.0
```

检查：

- [ ] ZIP 与 `.sha256` 同时生成；
- [ ] 解压后可由 `start.cmd` 启动；
- [ ] `wwwroot` 为当前构建；
- [ ] 发布树不含 Python、源码缓存、运行数据和密钥；
- [ ] README、许可证、第三方声明和必要部署文档完整。

## 7. GitHub Release

- [ ] 创建 `v1.1.0` 标签；
- [ ] 上传 ZIP 和 SHA-256；
- [ ] 发布说明引用对应 CHANGELOG；
- [ ] 明确生命周期控制仅支持 Windows 本机；
- [ ] 提醒升级前备份存档、PalOps 数据和 PalDefender 配置。

PalDefender 发布前的详细检查依据 [PalDefender 部署指南](paldefender-deployment.md)。
