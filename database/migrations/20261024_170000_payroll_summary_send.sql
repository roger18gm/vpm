create table if not exists public.payroll_summary_send (
    company_id integer not null references public.company (id) on delete cascade,
    week_start date not null,
    claimed_at timestamptz not null,
    sent_at timestamptz,
    primary key (company_id, week_start)
);
