# Системная тема по умолчанию — AT-26

31 октября 2026. [Issue #26](https://github.com/MimicBYE/AnimeTracker/issues/26), база dev e92f199. Стабильный 1.0.1 не изменяется.

В настройках обоих клиентов три варианта: **Системная / Светлая / Тёмная**. System — первый пункт и default при отсутствии файла/валидной копии. Прежний сохранённый Light/Dark не сбрасывается. Черновик применяется только кнопкой «Применить». Режим хранится в том же appearance-settings.xml и переносится существующими backup/XDG-механизмами.

Общий AppearanceBinding принимает источник ОС, UI-диспетчер и renderer. Он применяет текущую схему при запуске, реагирует на изменения и удаляет подписки при выходе. Повторный эффективный цвет игнорируется; отложенное событие использует актуальный ручной выбор, а после Dispose ничего не меняет. OS-сигналы не сохраняют XML и не уведомляют библиотеку. Окна, popup и виджет используют существующую палитру AT-24.

Windows читает только AppsUseLightTheme текущего пользователя (режим приложений, а не панели задач) и получает уведомления SystemEvents.UserPreferenceChanged. Avalonia читает PlatformSettings.GetColorValues и слушает ColorValuesChanged независимо от RequestedThemeVariant приложения. Смена собственной темы не выдаётся за смену темы ОС. Недоступная/неизвестная схема даёт Light; в Linux доступность сигнала зависит от окружения рабочего стола.

## Проверки

- Core и тот же набор в WPF/.NET Framework: default System, запуск при светлой и тёмной ОС, live OS change, ручной override обеих тем, повторные события, сохранение/перезапуск, отказ записи, отсутствие записи/библиотечных событий при OS change, очередь и гонка с ручным выбором, Dispose и отписка. Источник искусственный, ОС не меняется.
- WPF theme test: настоящие контролы и виджет в отдельном процессе до обычного OnStartup, выбор System из настроек, уведомление из фонового потока через Dispatcher, эффективный фон, manual override и неизменность XML библиотеки/настройки. Проверяется интерпретация значений реестра без их записи.
- Avalonia DesktopTests: настоящий MainWindow с внедрённым искусственным источником, Light/Dark/System, смена схемы из фонового потока, виджет, manual override, настройка System после перезапуска, отсутствие записи при системном событии, отсутствие утечки подписки после закрытия.
- Существующие Core/MAL/backup/пакетные тесты, SettingsFlow (черновик темы сохраняется при импорте/отмене), поиск 1000/5000 и эмуляция масштаба 150/200% сохраняются.
- Локально Windows: CoreTests, DesktopTests, WPF theme test и полный release-windows прошли (self/import/theme из binary ZIP; source rebuild и self/theme из пересобранного EXE). Verification: artifacts/release/cf18f85f631c/verification.json, в Git не включается. CI трёх ОС и все обязательные проверки должны пройти перед merge.

## Границы

Тесты не меняют личную data, реестр темы Windows, тему macOS/Linux или системную высокую контрастность. Нативное переключение ОС на физических Mac/Ubuntu/Mint, расписания автоматического переключения macOS, Spaces и Win+D вручную в AT-26 не проверялись. Подмена источника проверяет реакцию приложения, а не доставку нативного события конкретной оболочкой. Декорации и нативные меню/панели ОС сохраняют платформенные ограничения AT-24. Дополнительных зависимостей и polling нет.

Источники API: [Avalonia PlatformSettings](https://docs.avaloniaui.net/docs/services/platform-settings), [Microsoft SystemEvents](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents), [AppsUseLightTheme](https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/develop/settings/settings-common.md).
