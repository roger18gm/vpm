with inserted_templates as (
  insert into public.checklist_template (company_id, name, is_default)
  select company.id, 'Surface prep', true
  from public.company company
  where not exists (
    select 1
    from public.checklist_template existing
    where existing.company_id = company.id
  )
  returning id
)
insert into public.checklist_template_item (checklist_template_id, title, sort_order, is_required)
select inserted_templates.id, item.title, item.sort_order, true
from inserted_templates
cross join (
  values
    ('Sanding', 1),
    ('Masking', 2),
    ('Priming', 3),
    ('Cleanup', 4)
) as item(title, sort_order);
