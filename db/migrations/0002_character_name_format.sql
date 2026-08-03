-- 0002_character_name_format.sql — CHECK de longitud para characters.name, equivalente al
-- username_format de accounts (0001_init.sql) que se quedó sin pareja. Última línea de defensa:
-- la validación real de verdad (rango + charset) vive en el servidor (FASE-03-personajes.md §5).

ALTER TABLE characters
    ADD CONSTRAINT character_name_format CHECK (length(name) BETWEEN 3 AND 20);
