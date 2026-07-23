# 构建说明

> 语言：**简体中文** | [English](build.en.md)

## 工具链

- Windows 10/11 或 Windows Server（正式发布必需）；
- .NET 10 SDK；
- Node.js 22 与 npm；
- PowerShell 7 或 Windows PowerShell；

## 标准验证入口

在仓库根目录执行：

```powershell
.\scripts\build.ps1
```

该脚本依次完成：

1. `npm ci`；
2. 前端契约校验；
3. Vue TypeScript 检查；
4. Vite 生产构建到 `src/PalOps.Web/wwwroot`；
5. npm 高危依赖审计；
6. `.NET restore` 和 Release build；
7. 目录、地图、文档和源码树校验。

单独运行 `dotnet build` 不能作为完整发布证据。

## 前端开发

```powershell
cd frontend-vue
npm ci
npm run dev
```

类型检查与生产构建：

```powershell
npm run typecheck
npm run build
```

`npm run build` 会执行所有前端契约，包括地图首帧、PalDefender、停服流程、RCON、输入框样式、文档集和 GitHub 发布契约。

## .NET 构建

```powershell
dotnet restore PalOpsWeb.slnx
dotnet build PalOpsWeb.slnx -c Release --no-restore
```

`PalOps.Web` 和 `PalOps.Tooling` 均以 `net10.0` 为目标。正式生命周期烟雾测试仍必须在 Windows 上执行。

## GitHub Actions

`.github/workflows/build.yml` 使用当前 Node 24 action runtime 对应版本：

- `actions/checkout@v6`；
- `actions/setup-dotnet@v5`；
- `actions/setup-node@v6`。

CI 在 `ubuntu-latest` 上验证可移植编译、前端、文档和源码仓库。项目不需要 Python；源码校验和严格发布目录校验都会拒绝 Python 文件、运行数据、密钥和缓存。

## 固定地图数据维护

固定 POI 基线已经作为经过审查的 JSON 静态资产提交在 `frontend-vue/public/map/data`，仓库不再包含或依赖 Python 地图生成器。修改 POI、分类或坐标数据后，直接执行：

```powershell
cd frontend-vue
npm run verify:map-complete-local
npm run build
```

校验会确认三种运行时语言的数据规模、30 个分类、坐标范围和前端静态资源完整性。构建过程不会生成后端地图包、签名、激活文件或健康检查数据。

## 离线瓦片

正式发布前确认两套瓦片齐全：

```powershell
.\scripts\fetch-map-tiles.ps1 -Layer all
```

瓦片位于：

```text
frontend-vue/public/map/tiles/palpagos/
frontend-vue/public/map/tiles/world-tree/
```

## Windows 发布

```powershell
.\scripts\publish-win-x64.ps1 -Version 1.3.1
```

输出：

```text
artifacts/palops-web-1.3.1-win-x64/
artifacts/palops-web-1.3.1-win-x64.zip
artifacts/palops-web-1.3.1-win-x64.sha256
```

发布脚本会重新构建前端和后端，验证严格发布树，并生成 SHA-256。

## 常见失败

### `npm ci` 失败

确认 `frontend-vue/package.json` 与 `package-lock.json` 同步，不要手工删除锁文件条目。

### GitHub Action 在 `Verify source repository` 失败

确认仓库中不存在 `.py`、`setup-python` 或 Python 命令依赖。地图固定数据已经作为 JSON 静态资产提交，构建不应调用外部生成器。

### 地图校验失败

确认图层描述、三语言 POI、图标和瓦片目录完整。开发环境可使用 `map verify --allow-missing`，严格发布不能缺少瓦片。

### Windows 运行验证缺失

Linux CI 只能证明编译与静态契约。发布前仍需在非生产 Windows PalServer 上验证启动、正常停止、重启、强制停止、存档解析和 PalDefender。
