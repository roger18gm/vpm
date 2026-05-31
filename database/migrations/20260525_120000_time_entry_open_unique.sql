create unique index if not exists time_entry_one_open_per_person_idx
  on public.time_entry (person_id)
  where clock_out_at is null;
