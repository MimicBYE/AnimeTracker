# Защита репозитория и contributions

Настройки `MimicBYE/AnimeTracker` проверены 26 октября 2026 по [issue #7](https://github.com/MimicBYE/AnimeTracker/issues/7). Этот файл описывает фактическую политику GitHub; сам по себе он не включает защиту при копировании репозитория.

## Кто может вносить изменения

Репозиторий публичный. Любой может читать код, сделать fork и предложить pull request. Появление в списке Contributors или принятый PR не выдаёт право записи.

На дату проверки доступ на запись имеют `MimicBYE` (владелец/admin) и `QelBast` (write); ожидающих приглашений нет. Доступ сохранён без изменений. По решению владельца каждый из этих двух участников может самостоятельно сливать готовый PR без одобрения второго. Число обязательных approving reviews — **0**; обязательного CODEOWNERS approval и auto-merge нет. Внешний автор не получает право слияния своего PR.

Публикация здесь означает обычный путь `AT-N` → PR в `dev`, затем PR выпуска `dev` → `main`. Прямой push в защищённую ветку не используется. PR сообщества может идти из ветки fork с другим именем; соглашение `AT-N` относится к рабочим веткам проекта. Содержание проверяется по [CONTRIBUTING.md](../CONTRIBUTING.md), независимо от того, кто автор.

## main и dev

Для обеих веток включена одинаковая classic branch protection:

- Изменения только через pull request, базовая ветка должна быть актуальной (`strict` status checks).
- Перед merge все обязательные проверки должны пройти; результат принимается от приложения **GitHub Actions**, app ID `15368`.
- Обсуждения review должны быть разрешены. Обязательного второго approval нет; явно запрошенные изменения нужно разобрать перед merge.
- Force-push и удаление запрещены. Правила push/merge действуют и для администратора, исключений для обхода не задано.
- Полная блокировка веток (`lock_branch`) не включена: обычное слияние готового PR разрешено. Требование линейной истории не включено, merge-коммиты сохраняются.

Обязательные check contexts (точные имена):

```text
Release plan
Core (ubuntu-latest)
Core (windows-latest)
Core (macos-15)
Windows package
macOS package
Dependency audit / NuGet full audit
Verify release bundle
Review dependency changes
```

`Publish release` не требуется на PR: публикация выполняется после принятия изменений и намеренно skipped на PR. При изменении имён jobs/матрицы ОС синхронно обновлять required contexts в Settings; не удалять обязательные проверки ради merge. Если база изменилась, обновить PR из базы и дождаться нового CI. PR не становится готовым только от отметки автора или зелёного результата старого коммита.

## GitHub Actions

- Разрешены локальные workflows и внешние actions, принадлежащие GitHub. Остальные marketplace actions не разрешены текущим списком. Внешние actions обязаны быть закреплены полным commit SHA; существующие workflows соответствуют этой политике.
- Запуск workflow из public fork требует подтверждения участником с write для **всех внешних авторов** (`all_external_contributors`), даже если они уже присылали PR. Подтверждать запуск следует после просмотра изменений workflow/скриптов. Это одобрение запуска кода на runner, а не approving review для merge.
- `GITHUB_TOKEN` по умолчанию имеет read permissions; создание approving reviews через Actions запрещено. Только финальный job публикации явно получает `contents: write` после проверок. Подтверждение fork workflow не должно превращаться в выдачу токенов записи или секретов коду PR.
- Dependabot PR остаются обычными PR с проверками; auto-merge не включается.

## Границы защиты и обслуживание

Правила не дают приложению новых разрешений, не меняют публичность, лицензию, личные данные или существующие релизы. Слияние в `dev` по-прежнему запускает промежуточный выпуск по настроенному CI.

Защита относится к `main`/`dev`. Рабочие ветки, настройки репозитория и release assets не становятся неизменяемыми от branch protection. Политика неизменности опубликованных тегов/вложений остаётся в [SECURITY.md](../SECURITY.md) и скриптах публикации. Владелец с admin-доступом технически может изменить настройки GitHub; `enforce_admins` запрещает обход при push/merge, а не лишает владельца управления репозиторием.

После изменения прав, CI или правил ответственный проверяет через API/Settings: список участников/приглашений, защиту обеих веток, точные contexts и app ID, Actions allowlist/SHA pinning/fork approval, default token permissions. Не выполнять пробный force-push/удаление production-ветки ради проверки. Рабочий PR с успешным CI и merge под новыми правилами подтверждает, что разрешённый сценарий остаётся доступным.

Страницы настроек (нужен доступ владельца): [ветки](https://github.com/MimicBYE/AnimeTracker/settings/branches), [Actions](https://github.com/MimicBYE/AnimeTracker/settings/actions), [участники](https://github.com/MimicBYE/AnimeTracker/settings/access).

Основание: [protected branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches), [права personal repository](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/repository-access-and-collaboration/permission-levels-for-a-personal-account-repository), [настройки Actions](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/approve-runs-from-forks).
