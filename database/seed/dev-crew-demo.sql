-- VisionPaint dev seed: 1 manager + 3 crew logins
--
-- Prerequisites:
--   1. Migrations applied (company id 1 exists)
--   2. Owner bootstrap completed (POST /api/auth/bootstrap)
--
-- Password: same as the bootstrap owner (e.g. Password123!)
-- Run in Supabase SQL editor or psql against your dev database.
-- Safe to re-run — skips emails that already exist.

DO $$
DECLARE
  owner_password_hash text;
  new_auth_id uuid;
  new_person_id integer;
  seed record;
BEGIN
  SELECT password_hash
  INTO owner_password_hash
  FROM public.auth_user
  ORDER BY created_at
  LIMIT 1;

  IF owner_password_hash IS NULL THEN
    RAISE EXCEPTION 'No auth_user found. Run POST /api/auth/bootstrap first.';
  END IF;

  FOR seed IN
    SELECT *
    FROM (VALUES
      ('manager@visionpaint.local', 'Demo Manager', 'manager'),
      ('crew1@visionpaint.local', 'Alex Rivera', 'crew'),
      ('crew2@visionpaint.local', 'Jordan Lee', 'crew'),
      ('crew3@visionpaint.local', 'Sam Ortiz', 'crew')
    ) AS seed_users(email, name, role)
  LOOP
    IF EXISTS (SELECT 1 FROM public.auth_user WHERE email = seed.email) THEN
      RAISE NOTICE 'Skipping % — already exists', seed.email;
      CONTINUE;
    END IF;

    new_auth_id := gen_random_uuid();

    INSERT INTO public.auth_user (id, email, password_hash, is_active, created_at, updated_at)
    VALUES (new_auth_id, seed.email, owner_password_hash, true, now(), now());

    INSERT INTO public.person (auth_user_id, name, email, is_active, created_at, updated_at)
    VALUES (new_auth_id, seed.name, seed.email, true, now(), now())
    RETURNING id INTO new_person_id;

    INSERT INTO public.company_member (company_id, person_id, role, status, joined_at)
    VALUES (1, new_person_id, seed.role, 'active', now());

    RAISE NOTICE 'Created % (%) as %', seed.name, seed.email, seed.role;
  END LOOP;
END $$;

-- Verify:
-- SELECT p.name, p.email, cm.role
-- FROM public.person p
-- JOIN public.company_member cm ON cm.person_id = p.id
-- WHERE p.email LIKE '%@visionpaint.local'
-- ORDER BY cm.role, p.name;
