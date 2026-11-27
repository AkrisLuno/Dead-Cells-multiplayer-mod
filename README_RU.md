# Dead Cells Multiplayer Mod

[English](README.md)

Кооперативный мод для Dead Cells Update 35 на Windows. Один игрок создаёт лобби, остальные подключаются через LAN/TCP или Steam P2P.

## Установка

Распакуйте `win-x64.zip` из [DCCM v35.14.1](https://github.com/dead-cells-core-modding/core/releases/tag/v35.14.1) в папку `coremod` в корне игры. Поместите собранный мод в `coremod/mods/DeadCellsMultiplayerMod/`. Файл `modinfo.json` должен находиться непосредственно в папке мода. Всем игрокам нужна одна и та же сборка мода. Запустите `coremod/core/host/startup/DeadCellsModding.exe`.

## Подключение

- **LAN/TCP.** Хост выбирает **Play multiplayer → Host game** и передаёт код комнаты или IPv4-адрес и порт. Клиент выбирает **Play multiplayer → Join game**, вводит код или адрес и нажимает **Connect**. Порт по умолчанию — TCP `1234`.
- **Steam P2P.** Хост открывает **Steam & Advanced Options → Steam host**, а клиент — **Steam join**. Можно воспользоваться приглашением друга или вставить код Steam через **Join by Steam lobby code (fallback)**.

В лобби каждый игрок переключает **Ready: On**. Хост выбирает слот и запускает **Normal Mode**, **Custom Mode** или **Continue**. Сохранения мультиплеера лежат в `save/MSave/`.

Подробности об [установке](README.md#install), [сборке](CONTRIBUTING_RU.md) и [диагностике](docs/TROUBLESHOOTING.md).
