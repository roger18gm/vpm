create table if not exists public.refresh_token (
    id uuid primary key default gen_random_uuid(),
    auth_user_id uuid not null references public.auth_user(id) on delete cascade,
    session_id uuid not null,
    token_id uuid not null unique,
    expires_at timestamp with time zone not null,
    created_at timestamp with time zone not null default now(),
    revoked_at timestamp with time zone null,
    revoke_reason text null,
    replaced_by_token_id uuid null
);

create index if not exists refresh_token_auth_user_id_idx
    on public.refresh_token (auth_user_id);

create index if not exists refresh_token_session_id_idx
    on public.refresh_token (session_id);
