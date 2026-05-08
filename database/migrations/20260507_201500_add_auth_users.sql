create extension if not exists pgcrypto;

create table if not exists public.auth_user (
    id uuid primary key default gen_random_uuid(),
    email text not null unique,
    password_hash text not null,
    is_active boolean not null default true,
    email_confirmed_at timestamp with time zone null,
    last_login_at timestamp with time zone null,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now()
);

create unique index if not exists person_auth_user_id_key
    on public.person (auth_user_id)
    where auth_user_id is not null;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'person_auth_user_id_fkey'
    ) then
        alter table public.person
            add constraint person_auth_user_id_fkey
            foreign key (auth_user_id)
            references public.auth_user(id)
            on delete set null;
    end if;
end $$;
