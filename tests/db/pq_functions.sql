-- Тестовые функции для PQ-тестов (TASK-011).
-- Никаких побочных эффектов, кроме временных таблиц.

CREATE SCHEMA IF NOT EXISTS pq;

-- PQ-01: колонки разных типов.
CREATE OR REPLACE FUNCTION pq.mixed_types()
RETURNS TABLE(id integer, label text, amount numeric, happened date, enabled boolean, ratio double precision)
LANGUAGE sql STABLE AS $$
 SELECT 1, 'alpha', 12.50::numeric, DATE '2026-01-03', true, 0.5::double precision
 UNION ALL
 SELECT 2, 'beta', NULL::numeric, DATE '2026-02-04', false, NULL::double precision
 UNION ALL
 SELECT 3, 'gamma', 7.25::numeric, DATE '2026-03-05', true, 1.25::double precision
$$;

-- PQ-02/PQ-07: все поддерживаемые параметры + необязательный.
CREATE OR REPLACE FUNCTION pq.echo_params(p_date date, p_int integer, p_decimal numeric, p_text text, p_bool boolean, p_optional text DEFAULT NULL)
RETURNS TABLE(echo_date date, echo_int integer, echo_decimal numeric, echo_text text, echo_bool boolean, echo_optional text)
LANGUAGE sql IMMUTABLE AS $$
 SELECT p_date, p_int, p_decimal, p_text, p_bool, p_optional
$$;

-- PQ-04: кириллица и пробелы в именах колонок.
CREATE OR REPLACE FUNCTION pq.cyrillic_columns()
RETURNS TABLE("ОГРН" text, "ИНН" text, "Наименование организации" text, name_ text, name_full_ text)
LANGUAGE sql IMMUTABLE AS $$
 SELECT '111', '222', 'ООО Ромашка', 'Ромашка', 'Общество с ограниченной ответственностью Ромашка'
$$;

-- PQ-04 (характеризующий): character с дополнением пробелами.
CREATE OR REPLACE FUNCTION pq.character_padding()
RETURNS TABLE(padded character(10), plain text)
LANGUAGE sql IMMUTABLE AS $$
 SELECT 'abc'::character(10), 'abc'::text
$$;

-- PQ-06: аргумент называется иначе, чем параметр YAML.
CREATE OR REPLACE FUNCTION pq.different_argument_name(p_actual integer)
RETURNS TABLE(value integer)
LANGUAGE sql IMMUTABLE AS $$
 SELECT p_actual
$$;

-- PQ-08: долгий запрос для отмены.
CREATE OR REPLACE FUNCTION pq.slow_report()
RETURNS TABLE(value integer)
LANGUAGE plpgsql AS $$
BEGIN
 PERFORM pg_sleep(30);
 RETURN QUERY SELECT 1;
END
$$;

-- PQ-09: долгий запрос для таймаута (быстрее 30 с, чтобы тест не ждал)
CREATE OR REPLACE FUNCTION pq.slow_report_5()
RETURNS TABLE(value integer)
LANGUAGE plpgsql AS $$
BEGIN
 PERFORM pg_sleep(5);
 RETURN QUERY SELECT 1;
END
$$;

-- PQ-10: RAISE EXCEPTION.
CREATE OR REPLACE FUNCTION pq.failing_report()
RETURNS TABLE(value integer)
LANGUAGE plpgsql AS $$
BEGIN
 RAISE EXCEPTION 'pq synthetic failure';
END
$$;

-- PQ-11: пустой результат, но схема есть.
CREATE OR REPLACE FUNCTION pq.empty_result(p_none integer DEFAULT NULL)
RETURNS TABLE(id integer, label text)
LANGUAGE sql STABLE AS $$
 SELECT 1 AS id, 'x'::text AS label WHERE false
$$;

-- PQ-12: временная таблица, как в реальных отчётах.
CREATE OR REPLACE FUNCTION pq.temp_table_report(p_seed integer)
RETURNS TABLE(value integer)
LANGUAGE plpgsql AS $$
BEGIN
 CREATE TEMP TABLE IF NOT EXISTS pq_temp_result(value integer);
 TRUNCATE pq_temp_result;
 INSERT INTO pq_temp_result VALUES (p_seed), (p_seed + 1);
 RETURN QUERY SELECT t.value FROM pq_temp_result t ORDER BY t.value;
END
$$;

-- PQ-13: 6000 строк.
CREATE OR REPLACE FUNCTION pq.many_rows(p_count integer)
RETURNS TABLE(seq integer, label text)
LANGUAGE sql STABLE AS $$
 SELECT g AS seq, 'row ' || g AS label FROM generate_series(1, p_count) AS g
$$;