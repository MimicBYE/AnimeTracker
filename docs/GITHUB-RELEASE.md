# Подготовка релиза GitHub

Текущий номер подготовки — **1.1.1**, будущий стабильный тег — **v1.1.1**. Один комплект: Windows x64, Mac ARM64, Linux x64 и исходники. Старые опубликованные версии и вложения не заменяются.

## Штатный путь

1. Готовые issue-ветки проходят PR/CI и принимаются в dev.
2. В issue-ветке подготовки выполнить scripts/set-version.ps1 -Version 1.1.1, обновить README, инструкции, docs/releases/1.1.1.md и отчёт. Проверенный PR направить в dev.
3. Открыть релизный PR **dev → main**. Он выполняет Core/UI, аудит, упаковку трёх платформ, Windows rebuild из source ZIP и Verify. До merge stable не опубликован; готовый комплект доступен в Actions artifacts.
4. После решения о выпуске принять release PR. Push в main повторно проверяет точный коммит и после успеха публикует новый тег/вложения. Прямой push в main не требуется. Dev остаётся отдельным Pre-release.

[События CI, токены и неизменность](CI.md). [Первый переход с версии без updater](UPDATES.md).

## Итоговый комплект

- AnimeTracker-1.1.1-windows-x64.zip
- AnimeTracker-1.1.1-macos-arm64.zip
- AnimeTracker-1.1.1-linux-x64.tar.gz
- AnimeTracker-1.1.1-source.zip
- SHA256SUMS.txt

Описание страницы берётся из docs/releases/1.1.1.md. Пакеты включают matching BUILD-INFO (версия/канал/коммит/updateProtocol=1). Личные данные, SDK, кеши и промежуточные файлы не включаются.

Локальные упаковщики: scripts/release-windows.ps1, scripts/build-macos.ps1, scripts/build-linux.ps1. Нативные проверки выполняются на соответствующих ОС. scripts/prepare-release.ps1 собирает четыре архива одного контекста в новую папку с общими суммами и описанием, ничего не публикуя. Старый prepare-github.ps1 — исторический bootstrap, штатный CI его не использует.

Пример подготовки из проверенных артефактов одного стабильного контекста:

```powershell
./scripts/prepare-release.ps1 -InputDirectory artifacts/downloaded -OutputDirectory dist/release-1.1.1 -PackageVersion 1.1.1 -Channel stable -Commit <полный-SHA>
```

Не смешивать пакеты dev/local/stable или разных запусков. Публикует только последний CI job с GITHUB_TOKEN; PAT/ключи в исходниках и интерфейсе не нужны. Лицензия собственного кода не выбрана; сторонние уведомления сохранены.
