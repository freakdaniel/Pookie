# Браузерный процесс Pookie

ElectronNET.Core 0.6.0 + Electron 44.7.0. Основное приложение автоматически запускает
отдельный браузерный процесс для входа, библиотеки, поиска, чтения страниц треков,
лайков, репостов, подписок и редактирования плейлистов. MewUI и обычный аудиоплеер
остаются нативными.

## Основное приложение

```bash
dotnet build ./src/Pookie.App -p:PookieInstallElectron=true
dotnet run --no-build --project ./src/Pookie.App
```

Сборка копирует helper и Chromium в `BrowserHost/` рядом с Pookie; публикация
включает ту же папку. Helper использует private pipe для ответов, stdin для
команд и отдельный persistent профиль. OAuth/cookies не передаются в argv или
логи. На каждом запросе страница использует собственный `fetch`, а main-процесс
Electron проверяет origin и главный frame у IPC. Никакой зависимости InfiniFrame,
WebKitGTK P/Invoke или подмены идентичности браузера больше нет.

Если экспорт сессии отсутствует в хранилище паролей, приложение пробует восстановить
его из сохранённого профиля Electron в скрытом окне с ограниченным временем ожидания.
Вход сохраняет существующий профиль; явный выход сначала завершает worker,
затем очищает профиль Electron. Предыдущий профиль прототипа используется без
переноса cookies. Старые профили других движков автоматически не импортируются.

Electron по умолчанию не включает Widevine CDM. Браузерный EME-путь Windows
перенесён на Electron, но защищённое воспроизведение требует подходящего CDM и
отдельной проверки Windows. Linux использует прежний нативный Widevine/SoundFlow.

## Отдельная проверка браузера

Для первой установки нужны .NET 10, Node.js 22+ и npm. Запускай из корня репозитория:

```bash
dotnet build ./src/Pookie.Browser.Electron -p:PookieInstallElectron=true
dotnet run --no-build --project ./src/Pookie.Browser.Electron -- -unpackeddotnet
```

Откроется окно SoundCloud. Войди обычным способом; если сайт показывает проверку,
она проходит в этом же окне. После появления сессии процесс запросит `/me` через
`fetch` самой страницы, сохранит браузерное хранилище и завершится. Результат
`POOKIE_ELECTRON_RESULT` с `kind: "profile"` содержит ID/имя пользователя и версии
движка; OAuth-токены, cookies и транспортные секреты в консоль не выводятся.

Проверка после полного перезапуска, без открытия окна входа:

```bash
dotnet run --no-build --project ./src/Pookie.Browser.Electron -- -unpackeddotnet --verify-session --timeout 60
```

На Linux профиль находится в `$XDG_DATA_HOME/Pookie/Browser/Electron`, а при
отсутствии абсолютного `XDG_DATA_HOME` — в `~/.local/share/Pookie/Browser/Electron`.
Он сохраняется между запусками. Это отдельный профиль: первый вход потребуется
повторить. Отдельная диагностическая команда не сохраняет экспорт сессии в хранилище приложения. Параметр
`--profile <путь>` позволяет проверить другой изолированный профиль; одновременное
использование одного профиля блокируется.

Обычный `dotnet build Pookie.slnx` собирает .NET-код без установки Node/Chromium.
Для установки браузерного runtime используй команду сборки с
`-p:PookieInstallElectron=true`, приведённую выше.

## Поведение и границы этапа

- Один persistent partition `persist:soundcloud` для входа, cookies и запросов.
- Сессия наблюдается в запросах самого сайта. `fetch`, `Function.toString`,
  `navigator`, Client Hints и другие параметры браузера не переопределяются.
- Страница работает с `nodeIntegration: false`, `contextIsolation: true`,
  `sandbox: true`, `webSecurity: true`. У preload только две фиксированные функции;
  main-процесс проверяет окно, origin и главный frame перед каждой операцией.
- HTTPS-окна провайдеров входа используют тот же профиль и изоляцию. Операции
  с файлами, webview и разрешения устройства для сайта закрыты.
- Нет циклической перезагрузки сайта и повторов при HTTP-ошибке `/me`. Успешный
  локальный тест не доказывает, что SoundCloud одобрит конкретную сеть/сессию.

Коды завершения: `0` — профиль получен; `1` — закрытие/таймаут/отмена;
`2` — аргументы или запуск runtime; `3` — профиль занят; `4` — ограничение сайта
или ошибка `/me`. Окно входа ждёт до пяти минут; `--timeout` задаёт 1–600 секунд.

## Passkey-вход на Linux

В Electron 44.7.0 нет Chrome-диалога для подтверждения passkey на телефоне через
QR-код (WebAuthn hybrid/caBLE). Страница Google может предложить такой вход, но
сам браузерный процесс не покажет ожидаемое окно. Разрешение OAuth-попапов этого
не исправляет. Наличие движка Chromium не означает наличие всех компонентов
интерфейса и входа Chrome.

Проверено по исходникам используемых версий: в
[Chrome ConfigureDiscoveries](https://github.com/chromium/chromium/blob/152.0.7977.130/chrome/browser/webauthn/chrome_authenticator_request_delegate.cc)
генерирует QR-данные, настраивает caBLE и передаёт их контроллеру диалога.
[Делегат Electron](https://github.com/electron/electron/blob/v44.7.0/shell/browser/webauthn/electron_authenticator_request_client_delegate.h)
этот метод не реализует; его
[базовая реализация Chromium](https://github.com/chromium/chromium/blob/152.0.7977.130/content/public/browser/authenticator_request_client_delegate.cc)
пустая. Это ограничение именно входа через телефон; оно не означает отсутствия
всех вариантов WebAuthn на всех платформах.

В окне входа выбирай в Google «Try another way» и доступный для аккаунта
другой способ подтверждения. Поддержка QR-входа потребует отдельной реализации
hybrid-транспорта и диалога либо полноценного браузера для авторизации; одним
параметром BrowserWindow или разрешением устройства её включить нельзя.

## Проверка без SoundCloud

```bash
node tests/electron-browser.smoke.cjs
```

Нужны установленный runtime и доступ к графической сессии. Тест использует
локальный HTTP-сайт и временные профили, проверяет изоляцию страницы, сохранение
HttpOnly-cookie и localStorage, новый процесс и авторизованный `/me`, отсутствие
сессии и отсутствие секретов в выводе. Также проверяется OAuth-попап: сначала
`about:blank`, затем переход на страницу провайдера; доступ к Node и открытие
`file://` остаются запрещены. Обращений к SoundCloud нет.

10 октября 2026 на Linux проверен и настоящий SoundCloud: вход, ответ `/me` с
HTTP 200, полное завершение процесса и повторный `/me` с HTTP 200 в новом скрытом
процессе без повторного входа. Движок: Electron 44.7.0 / Chromium 152.0.7977.130.
Это подтверждает работу данного профиля, а не отсутствие проверок для всех сетей.

Основания для реализации: [запуск консольного Electron.NET приложения](https://github.com/ElectronNET/Electron.NET/wiki/Startup-Methods),
[custom_main.js](https://github.com/ElectronNET/Electron.NET/blob/main/docs/Using/Custom_main.md),
[изоляция Electron](https://www.electronjs.org/docs/latest/tutorial/security),
[persistent partitions](https://www.electronjs.org/docs/latest/api/session#sessionfrompartitionpartition-options).
