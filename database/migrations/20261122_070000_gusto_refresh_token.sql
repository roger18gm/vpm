create table if not exists public.gusto_refresh_token (
    id integer primary key,
    refresh_token text not null,
    updated_at timestamptz not null default now(),
    constraint gusto_refresh_token_one_row check (id = 1)
);

alter table public.gusto_refresh_token enable row level security;
