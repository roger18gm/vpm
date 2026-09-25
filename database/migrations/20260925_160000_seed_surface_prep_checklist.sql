insert into public.checklist_template (company_id, name, is_default)
select company.id, 'Surface prep', true
from public.company company
where not exists (
  select 1
  from public.checklist_template existing
  where existing.company_id = company.id
);

insert into public.checklist_template_item (checklist_template_id, title, sort_order, is_required)
select template.id, item.title, item.sort_order, true
from public.checklist_template template
cross join (
  values
    ('Sanding', 1),
    ('Masking', 2),
    ('Priming', 3),
    ('Cleanup', 4)
) as item(title, sort_order)
where template.name = 'Surface prep'
  and template.is_default = true
  and not exists (
    select 1
    from public.checklist_template_item existing
    where existing.checklist_template_id = template.id
  );
