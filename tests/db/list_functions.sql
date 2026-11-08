-- Функции для отчётов по списку (TASK-023). Побочных эффектов нет.

CREATE SCHEMA IF NOT EXISTS lst;

CREATE TABLE IF NOT EXISTS lst.orgs(inn text PRIMARY KEY, name text, city text);

TRUNCATE lst.orgs;
INSERT INTO lst.orgs(inn, name, city) VALUES
 ('7707083893', 'Ромашка', 'Москва'),
 ('500100732259', 'Лютик', 'Казань'),
 ('1111111111', 'Дубликат', 'Москва');

-- Сценарий А: ключ + общий параметр (дата).
CREATE OR REPLACE FUNCTION lst.by_inn(p_inn text[], p_row_id int[], p_asof date)
RETURNS TABLE(inn text, name text, city text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT o.inn, o.name, o.city, r.rid
 FROM unnest(p_inn, p_row_id) AS r(inn, rid)
 JOIN lst.orgs o ON o.inn = r.inn
 ORDER BY r.rid
$$;

-- Сценарий Б: ключ + дата на строку (параметр используется как фильтр по городу).
CREATE OR REPLACE FUNCTION lst.by_inn_date(p_inn text[], p_dt date[], p_row_id int[], p_asof date)
RETURNS TABLE(inn text, name text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT o.inn, o.name, r.rid
 FROM unnest(p_inn, p_dt, p_row_id) AS r(inn, dt, rid)
 JOIN lst.orgs o ON o.inn = r.inn
 WHERE r.dt <= p_asof
 ORDER BY r.rid
$$;

-- Ненайденные строки: возвращает только существующие (проверка статуса «Не найдено»).
CREATE OR REPLACE FUNCTION lst.found_only(p_inn text[], p_row_id int[])
RETURNS TABLE(inn text, name text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT o.inn, o.name, r.rid
 FROM unnest(p_inn, p_row_id) AS r(inn, rid)
 JOIN lst.orgs o ON o.inn = r.inn
 ORDER BY r.rid
$$;

-- 1:N: возвращает по две строки на каждый ключ.
CREATE OR REPLACE FUNCTION lst.one_to_many(p_inn text[], p_row_id int[])
RETURNS TABLE(inn text, variant text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT o.inn, v.variant, r.rid
 FROM unnest(p_inn, p_row_id) AS r(inn, rid)
 JOIN lst.orgs o ON o.inn = r.inn
 CROSS JOIN (VALUES ('A'), ('B')) AS v(variant)
 ORDER BY r.rid, v.variant
$$;

-- Неотсортированный результат.
CREATE OR REPLACE FUNCTION lst.unsorted(p_inn text[], p_row_id int[])
RETURNS TABLE(inn text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT o.inn, r.rid
 FROM unnest(p_inn, p_row_id) AS r(inn, rid)
 JOIN lst.orgs o ON o.inn = r.inn
 ORDER BY r.rid DESC
$$;

-- Чужой row_id.
CREATE OR REPLACE FUNCTION lst.foreign_row(p_inn text[], p_row_id int[])
RETURNS TABLE(inn text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT r.inn, r.rid + 1000
 FROM unnest(p_inn, p_row_id) AS r(inn, rid)
 ORDER BY r.rid
$$;

-- Перегруженная функция.
CREATE OR REPLACE FUNCTION lst.overloaded(p_inn text[], p_row_id int[])
RETURNS TABLE(inn text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT r.inn, r.rid FROM unnest(p_inn, p_row_id) AS r(inn, rid) ORDER BY r.rid
$$;

CREATE OR REPLACE FUNCTION lst.overloaded(p_inn text[], p_row_id int[], p_extra int)
RETURNS TABLE(inn text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT r.inn, r.rid FROM unnest(p_inn, p_row_id) AS r(inn, rid) ORDER BY r.rid
$$;

-- Несовпадение типа массива: ожидается int[], объявлен text[].
CREATE OR REPLACE FUNCTION lst.bad_type(p_inn text[], p_row_id int[])
RETURNS TABLE(inn text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT r.inn, r.rid FROM unnest(p_inn, p_row_id) AS r(inn, rid) ORDER BY r.rid
$$;

-- Долгая функция для отмены.
CREATE OR REPLACE FUNCTION lst.slow(p_inn text[], p_row_id int[])
RETURNS TABLE(inn text, row_id int)
LANGUAGE plpgsql AS $$
BEGIN
 PERFORM pg_sleep(30);
 RETURN QUERY SELECT r.inn, r.rid FROM unnest(p_inn, p_row_id) AS r(inn, rid);
END
$$;
-- Для теста ошибок ввода: принимает ИНН и дату на строку.
CREATE OR REPLACE FUNCTION lst.bad_date(p_inn text[], p_dt date[], p_row_id int[])
RETURNS TABLE(inn text, name text, row_id int)
LANGUAGE sql STABLE AS $$
 SELECT o.inn, o.name, r.rid
 FROM unnest(p_inn, p_dt, p_row_id) AS r(inn, dt, rid)
 LEFT JOIN lst.orgs o ON o.inn = r.inn
 ORDER BY r.rid
$$;