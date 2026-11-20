import { useMemo, useState } from "react";
import { Avatar, Combobox, Option } from "@fluentui/react-components";
import { initials } from "../context";
import type { UserRef } from "../types";

interface Props {
  users: UserRef[];
  value: UserRef | null;
  onChange: (user: UserRef | null) => void;
  placeholder?: string;
}

/** Personenauswahl mit Suche nach Name, E-Mail oder Funktion. */
export function PersonPicker({ users, value, onChange, placeholder }: Props) {
  const [query, setQuery] = useState<string | null>(null);

  const filtered = useMemo(() => {
    const q = (query ?? "").toLowerCase();
    const list = q
      ? users.filter((u) => u.name.toLowerCase().includes(q) || u.email?.toLowerCase().includes(q) || u.jobTitle?.toLowerCase().includes(q))
      : users;
    return list.slice(0, 50);
  }, [users, query]);

  return (
    <Combobox
      placeholder={placeholder ?? "Person suchen"}
      value={query ?? value?.name ?? ""}
      selectedOptions={value ? [String(value.id)] : []}
      onChange={(e) => setQuery(e.target.value)}
      onOptionSelect={(_, d) => {
        setQuery(null);
        onChange(users.find((u) => String(u.id) === d.optionValue) ?? null);
      }}
      onBlur={() => setQuery(null)}
    >
      {filtered.map((u) => (
        <Option key={u.id} value={String(u.id)} text={u.name}>
          <Avatar size={20} name={u.name} initials={initials(u.name)} color="colorful" style={{ marginRight: 8 }} />
          {u.name}
          {u.jobTitle ? ` · ${u.jobTitle}` : ""}
        </Option>
      ))}
    </Combobox>
  );
}
