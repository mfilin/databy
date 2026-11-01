CREATE SCHEMA reports;
CREATE TABLE items(id integer PRIMARY KEY, label text, amount numeric, happened date, enabled boolean);
INSERT INTO items VALUES
 (1, 'alpha', 12.50, DATE '2026-01-03', true),
 (2, 'beta', NULL, DATE '2026-02-04', false);
CREATE FUNCTION report_rows(p_after date, p_label text)
RETURNS TABLE(id integer, label text, amount numeric, happened date, enabled boolean)
LANGUAGE sql AS $$
 SELECT i.id, i.label, i.amount, i.happened, i.enabled
 FROM items i WHERE i.happened >= p_after AND i.label = p_label ORDER BY i.id
$$;
CREATE FUNCTION slow_report()
RETURNS integer LANGUAGE plpgsql AS $$
BEGIN PERFORM pg_sleep(30); RETURN 1; END
$$;
CREATE FUNCTION fail_report()
RETURNS integer LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'synthetic failure'; END
$$;
