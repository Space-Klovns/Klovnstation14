# KS14: automatically translated; see _KS14/Localization/translation-review.tsv.

cmd-zlevel_add-completion = <UID карты>
cmd-zlevel_add-desc = Устанавливает уровень z выше другого уровня z. Сделаем любую карту z-уровнем, если они еще этого не сделали. Второй параметр — это z-уровень, который будет выше.
cmd-zlevel_add-help = zlevel_add <целевой uid> <добавленный uid>
cmd-zlevel_add-invalid-args = Ожидается ровно 2 аргумента.
cmd-zlevel_add-invalid-uid = Неверный объект { $pretty }.
cmd-zlevel_elevator-completion-grid = <UID сетки>
cmd-zlevel_elevator-completion-shaft = <идентификатор вала>
cmd-zlevel_elevator-desc = Превращает существующую сетку в лифт, который может перемещаться вверх и вниз по стеку z-уровней. Необязательный второй параметр задает идентификатор шахты, который необходим только в том случае, если в одном стеке находится более одного лифта.
cmd-zlevel_elevator-help = zlevel_elevator <идентификатор сетки> [shaft id]
cmd-zlevel_elevator-invalid-args = Ожидается 1 или 2 аргумента.
cmd-zlevel_elevator-no-zlevel = Сделал это лифтом, но его карта еще не является z-уровнем, поэтому этажей на нем нет. Сначала свяжите карты с помощью zlevel_add.
cmd-zlevel_elevator-not-a-grid = { $uid } не является сеткой.
cmd-zlevel_elevator-success = Сделал лифт. Он служит { $floors } этаж(а) и в настоящее время находится на этаже { $floor }.
cmd-zlevel_elevator_controller-desc = Создает консоль контроллера лифта там, где вы стоите. Необязательный параметр привязывает его к идентификатору шахты, а не к ближайшему немаркированному лифту.
cmd-zlevel_elevator_controller-help = zlevel_elevator_controller [shaft id]
cmd-zlevel_elevator_controller-invalid-args = Ожидается не более 1 аргумента.
cmd-zlevel_elevator_controller-no-body = Вам понадобится тело, рядом с которым можно разместить контроллер.
cmd-zlevel_elevator_controller-success = Поставил контроллер. Он решает поднять { $elevator }.
cmd-zlevel_elevator_controller-unbound = Поставил контроллер, но он не разрешает лифт. Проверьте идентификатор шахты или убедитесь, что лифт находится в том же стеке z-уровня.
