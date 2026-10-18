# Состояния и переходы

## Игра

Registration → Open → Review → Finished. Назад нельзя в MVP.

| Команда | Условия | Результат |
| --- | --- | --- |
| OpenGame | SuperAdmin, Registration, валидная production-конфигурация | Open |
| CloseIntake | SuperAdmin, Open, подтверждение | Review, фиксируется ClosedAt |
| Finalize | SuperAdmin, Review, PendingCount=0 | Finished + snapshot |
| ForceFinalize | SuperAdmin, Review, отдельное подтверждение | Pending→Unreviewed, Finished + snapshot |

Финал и решение по заявке сериализованы общим game-state механизмом/транзакцией: после фиксации Finished апрув невозможен. Если апрув закоммичен раньше форса, его баллы входят в snapshot. Повторный финал не создаёт новый итог.

## Попытка задания

Невзятое — QueueEntry, это не начальное состояние уже существующей попытки.

- GetTask создаёт Attempt=Active.
- Active + SkipConfirmed → Skipped (terminal), возврат в очередь, N1.
- Active + SubmitConfirmed → Submitted (terminal для редактирования пользователем), Submission=Pending, N2.
- Submitted отображает состояние связанной Submission: Pending/Approved/Rejected/Unreviewed.

Историю не переписывать. Следующая выдача того же определения — новый Attempt ID. Статусы «выполненное» и «апрувнутое» в UI соответствуют Pending и Approved, не требуют дублировать противоречивые поля Attempt и Submission.

## Заявка

Pending → Approved ИЛИ Rejected ИЛИ Unreviewed. Переход только один. Unreviewed только при ForceFinalize. Все терминальные состояния неизменяемы в MVP.

## Кандидат фотоответа

Отсутствует → CollectingAlbum/Ready → Confirmed или Replaced/Cancelled. Только Ready может стать Submission. Candidate ID/version входит в callback. Новая фотография/альбом инвалидирует прежний кандидат. Старые кнопки возвращают понятный ответ и ничего не меняют.

Данные кандидата и известные части альбома переживают рестарт; временное debounce-окно восстанавливается по сохранённым timestamps. Подтверждение после закрытия приёма запрещено независимо от возраста кандидата.

## Дисквалификация и кулдаун

DQ — независимый флаг участника, не стирает очереди/историю. Pending проверять разрешено. Восстановление не сбрасывает время ожидания. При закрытии игры Active сохраняется для истории, но не принимается к выполнению; нет автоматического Skip или штрафа.

NextTaskAvailableAt — общий порог выдачи после последнего Skip/Submit. Просмотр задания/рейтинга, Reject/Approve, повторный callback и рестарт его не продлевают. DQ запрещает игровые команды независимо от порога.
