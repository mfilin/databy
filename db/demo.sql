CREATE SCHEMA IF NOT EXISTS reports;
CREATE TABLE IF NOT EXISTS reports.demo_sales (
 id bigserial PRIMARY KEY, sold_at date NOT NULL, product text NOT NULL,
 category text NOT NULL, quantity integer NOT NULL, unit_price numeric(12,2) NOT NULL, region text NOT NULL
);
TRUNCATE reports.demo_sales RESTART IDENTITY;
INSERT INTO reports.demo_sales(sold_at,product,category,quantity,unit_price,region) VALUES
('2026-10-01','Ноутбук Pro','Электроника',2,89990,'Москва'),('2026-10-02','Мышь беспроводная','Аксессуары',8,2490,'Казань'),
('2026-10-03','Монитор 27','Электроника',3,32990,'Москва'),('2026-10-04','Клавиатура','Аксессуары',5,5990,'Санкт-Петербург'),
('2026-10-05','Док-станция','Аксессуары',2,12990,'Казань'),('2026-10-06','Наушники','Электроника',4,11990,'Москва'),
('2026-10-07','Веб-камера','Электроника',6,7990,'Санкт-Петербург'),('2026-10-08','Кресло офисное','Мебель',1,28990,'Москва'),
('2026-10-09','Стол рабочий','Мебель',2,45990,'Казань'),('2026-10-10','Коврик для мыши','Аксессуары',12,790,'Санкт-Петербург');
CREATE OR REPLACE FUNCTION reports.fn_sales(p_from date, p_to date)
RETURNS TABLE(sold_at date, product text, category text, quantity integer, unit_price numeric, total numeric, region text)
LANGUAGE sql STABLE AS $$
 SELECT s.sold_at,s.product,s.category,s.quantity,s.unit_price,(s.quantity*s.unit_price)::numeric(14,2),s.region
 FROM reports.demo_sales s WHERE s.sold_at BETWEEN p_from AND p_to ORDER BY s.sold_at,s.product
$$;

CREATE OR REPLACE FUNCTION reports.fn_sales_leaders(p_from date, p_to date)
RETURNS TABLE(product text, category text, quantity bigint, revenue numeric, deals bigint)
LANGUAGE sql STABLE AS $$
 SELECT s.product, s.category, sum(s.quantity)::bigint,
        sum(s.quantity*s.unit_price)::numeric(14,2), count(*)::bigint
 FROM reports.demo_sales s WHERE s.sold_at BETWEEN p_from AND p_to
 GROUP BY s.product, s.category
 ORDER BY sum(s.quantity*s.unit_price) DESC, s.product
$$;

CREATE OR REPLACE FUNCTION reports.fn_city_sales_leaders(p_from date, p_to date)
RETURNS TABLE(region text, quantity bigint, revenue numeric, deals bigint, top_product text)
LANGUAGE sql STABLE AS $$
 SELECT s.region, sum(s.quantity)::bigint, sum(s.quantity*s.unit_price)::numeric(14,2), count(*)::bigint,
        (array_agg(s.product ORDER BY s.quantity*s.unit_price DESC))[1]
 FROM reports.demo_sales s WHERE s.sold_at BETWEEN p_from AND p_to
 GROUP BY s.region
 ORDER BY sum(s.quantity*s.unit_price) DESC, s.region
$$;

CREATE OR REPLACE FUNCTION reports.fn_top_products(p_limit integer, p_region text DEFAULT NULL)
RETURNS TABLE(product text, quantity bigint, revenue numeric)
LANGUAGE sql STABLE AS $$
 SELECT s.product, sum(s.quantity)::bigint, sum(s.quantity*s.unit_price)::numeric(14,2)
 FROM reports.demo_sales s
 WHERE (p_region IS NULL OR p_region = '' OR s.region = p_region)
 GROUP BY s.product
 ORDER BY 3 DESC
 LIMIT p_limit
$$;