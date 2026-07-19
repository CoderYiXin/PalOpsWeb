# 用户 ID/名称清单

`source` 目录保存用户提供的六份原始 JSON 字典，作为可追溯输入：

- `items_id_to_name.json`
- `pals_id_to_name.json`
- `technology_id_to_name.json`
- `structures_id_to_name.json`
- `passives_id_to_name.json`
- `skills_id_to_name.json`

原始数据包含未本地化占位符、未解析模板和少量错误机翻，因此程序不会直接读取这些源文件。运行：

```powershell
dotnet run --project tools\PalOps.Tooling -- catalog merge-names --root .
dotnet run --project tools\PalOps.Tooling -- catalog verify --root .
```

.NET 工具会执行以下处理：

1. 拒绝 `zh-hans text`、`Unknown(...)`、纯英文占位和未解析标记。
2. 保留现有更完整的 Boss、突袭、NPC 和内部变体名称。
3. 为装备、武器和设计图补齐可靠的 `+1～+4` 品质后缀。
4. 将清洗后的六类映射写入 `src/PalOps.Web/Seed/NameMaps`。
5. 将详细采用、拒绝和跳过记录写入 `docs/id-name-list-merge-audit.json`。

运行时只读取清洗后的离线映射，不会访问外部网站。
