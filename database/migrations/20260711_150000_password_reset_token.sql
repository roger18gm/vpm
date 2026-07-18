create table if not exists public.password_reset_token (
    id uuid primary key default gen_random_uuid(),
    auth_user_id uuid not null references public.auth_user(id) on delete cascade,
    token_hash text not null,
    expires_at timestamp with time zone not null,
    used_at timestamp with time zone null,
    created_at timestamp with time zone not null default now()
);

create unique index if not exists password_reset_token_token_hash_uidx
    on public.password_reset_token (token_hash);

create index if not exists password_reset_token_auth_user_id_idx
    on public.password_reset_token (auth_user_id);
