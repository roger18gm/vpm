create unique index if not exists gusto_employee_link_company_employee_uidx
    on public.gusto_employee_link (company_id, gusto_employee_uuid);
