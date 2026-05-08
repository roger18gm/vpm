# Database Workflow Standard

This project uses two different SQL artifacts for two different jobs.

## 1. `schema.sql`

`schema.sql` is the current, human-readable snapshot of the intended database shape.

Use it to:

- understand the current model quickly
- review table relationships
- draft the next round of changes before writing migrations

Do not treat it as the migration history. It is a reference document and should stay readable.

## 2. `migrations/`

Each real database change should live in its own migration file under `database/migrations/`.

Use individual migration files for:

- adding a table
- changing a column
- adding or dropping a foreign key
- adding an index
- creating or altering a policy

## Standard rule

- Make the change in a dedicated migration file.
- Keep migrations additive and small when possible.
- Do not edit an already-applied migration.
- If a later change depends on an earlier one, create a new migration rather than rewriting history.
- After the migration is finalized, update `schema.sql` so it matches the current intended end state.

## Initial reset note

If the existing database only contains throwaway mock data, the first migration can replace that scaffold with the real schema. After that initial reset, future changes should be additive again.

## Environment setup

Any future local, dev, or test database should be created by applying the full migration chain in order.

- **Local**: spin up an empty database and apply migrations.
- **Dev**: apply the same migrations to the dev database or Supabase branch.
- **Test**: apply the same migrations, then load test-specific seed data.

Do not use `schema.sql` as the thing that creates those environments. It is a reference snapshot, not the build mechanism.

## Naming convention

Use timestamped, snake_case migration names, for example:

- `20260507_183000_create_company_and_client_tables.sql`
- `20260507_184500_add_job_status_history.sql`

## Practical workflow

1. Draft the change in `schema.sql` or in notes.
2. Create a new migration file in `database/migrations/`.
3. Apply it to Supabase.
4. Verify the tables, constraints, and relationships.
5. Update `schema.sql` to reflect the final model.

## Why this works

This keeps the schema easy to read, keeps database history honest, and makes it much easier to review or roll forward future changes without guessing what happened when.
