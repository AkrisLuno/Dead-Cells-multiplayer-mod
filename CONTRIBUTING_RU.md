# Участие в разработке

См. [руководство по разработке](CONTRIBUTING.md) для описания структуры проекта и проверки мультиплеера.

Для сборки нужны Windows x64, .NET 10 SDK и полный архив [DCCM v35.14.1](https://github.com/dead-cells-core-modding/core/releases/tag/v35.14.1). В PowerShell задайте путь к установленному MDK и выполните команды из корня репозитория:

```powershell
$env:DCCM_MDK_ROOT = 'C:\Games\Dead Cells\coremod\core\mdk'
dotnet build -c Release
dotnet test -c Release --no-build
```

Готовый мод находится в `src/bin/Release/net10.0/win-x64/output/DeadCellsMultiplayerMod/`. Для проверки в игре следуйте [инструкции по установке](README_RU.md#установка).
