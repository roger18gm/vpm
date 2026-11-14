create table if not exists public.gusto_employee_link (
    person_id integer primary key references public.person (id) on delete cascade,
    company_id integer not null references public.company (id) on delete cascade,
    gusto_employee_uuid text not null,
    gusto_job_uuid text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists public.gusto_time_sheet_sync (
    time_entry_id integer primary key references public.time_entry (id) on delete cascade,
    gusto_time_sheet_uuid text,
    status text not null check (status in ('pending', 'synced', 'failed')),
    hours_worked numeric(8, 4) not null check (hours_worked >= 0),
    error_message text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);