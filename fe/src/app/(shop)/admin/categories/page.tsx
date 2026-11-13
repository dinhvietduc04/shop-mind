"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { AdminTable, Panel, Pill, statusBand, td, th } from "@/components/admin/widgets";
import { Button, ErrorNote, Skeleton } from "@/components/ui";
import { useAdminCategories, useDeleteCategory, useSaveCategory } from "@/features/admin";
import { ApiError, type CategoryDto } from "@/infrastructure/api";
import type { CreateCategoryRequest } from "@/lib/api/contracts/schemas";
import { BAND_BG, bandForCategory } from "@/lib/bands";
import { slugify } from "@/lib/slug";

const input =
  "w-full rounded-[4px] border-[1.5px] border-line bg-paper px-2 py-1.5 font-mono text-[12px] focus:border-ink";

export default function AdminCategoriesPage() {
  const { data, isPending } = useAdminCategories();
  const save = useSaveCategory();
  const del = useDeleteCategory();
  const [showNew, setShowNew] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const errText = (e: unknown) => {
    if (!(e instanceof ApiError)) return "Unexpected error.";
    const detail = Object.values(e.errors ?? {}).flat()[0];
    return detail ? `${e.title}. ${detail}` : e.title;
  };

  return (
    <div className="mt-5">
      <Panel
        title={`Categories · ${data?.length ?? "…"}`}
        aside={
          <Button variant="add" onClick={() => setShowNew((v) => !v)}>
            <Plus size={13} aria-hidden /> New category
          </Button>
        }
      >
        {showNew && (
          <NewCategoryForm
            busy={save.isPending}
            onSubmit={(c) => save.mutate(c, { onSuccess: () => { setShowNew(false); setError(null); }, onError: (e) => setError(errText(e)) })}
          />
        )}
        {isPending ? (
          <Skeleton className="h-48" />
        ) : (
          <AdminTable
            head={
              <>
                <th className={th}>Band</th>
                <th className={th}>Name</th>
                <th className={th}>Slug</th>
                <th className={th}>Description</th>
                <th className={th}>Active</th>
                <th className={th}>Actions</th>
              </>
            }
          >
            {data?.map((c) => (
              <CategoryRow
                key={c.id}
                category={c}
                busy={save.isPending || del.isPending}
                onSave={(patch) => save.mutate({ ...patch, id: c.id }, { onError: (e) => setError(errText(e)) })}
                onDelete={() => del.mutate(c.id, { onError: (e) => setError(errText(e)) })}
              />
            ))}
          </AdminTable>
        )}
        {error && (
          <div className="mt-3">
            <ErrorNote><strong>Write refused.</strong> {error}. Deleting a bin that still holds products is illegal, exactly like the real FK.</ErrorNote>
          </div>
        )}
      </Panel>
    </div>
  );
}

function NewCategoryForm({
  busy,
  onSubmit,
}: {
  busy: boolean;
  onSubmit: (c: CreateCategoryRequest) => void;
}) {
  const [name, setName] = useState("");
  return (
    <form
      className="mb-4 flex flex-wrap items-center gap-2 rounded-[4px] border-[1.5px] border-dashed border-line px-3 py-2"
      onSubmit={(e) => {
        e.preventDefault();
        if (!name.trim()) return;
        onSubmit({ name: name.trim(), slug: null, description: null, isActive: true });
      }}
    >
      <input value={name} onChange={(e) => setName(e.target.value)} placeholder="new bin name, slug auto-generates" className={`${input} w-[260px]`} aria-label="New category name" />
      <Button variant="human" type="submit" disabled={busy || !name.trim()}>
        Create
      </Button>
    </form>
  );
}

function CategoryRow({
  category,
  busy,
  onSave,
  onDelete,
}: {
  category: CategoryDto;
  busy: boolean;
  onSave: (patch: CreateCategoryRequest) => void;
  onDelete: () => void;
}) {
  const [form, setForm] = useState({
    name: category.name,
    slug: category.slug,
    description: category.description ?? "",
    isActive: category.isActive,
  });
  const dirty =
    form.name !== category.name ||
    form.slug !== category.slug ||
    form.description !== (category.description ?? "") ||
    form.isActive !== category.isActive;
  const band = BAND_BG[bandForCategory(form.slug || category.slug)];

  return (
    <tr className="border-b border-dashed border-line">
      <td className={td}>
        <span className={`inline-block h-4 w-4 rounded-[3px] border-[1.5px] border-[#1a1a1a] ${band}`} title={`band for /${form.slug}`} aria-hidden />
      </td>
      <td className={td}>
        <input value={form.name} onChange={(e) => setForm((x) => ({ ...x, name: e.target.value }))} className={input} aria-label={`Name of ${category.name}`} />
      </td>
      <td className={td}>
        <input
          value={form.slug}
          onChange={(e) => setForm((x) => ({ ...x, slug: slugify(e.target.value) }))}
          className={input}
          aria-label={`Slug of ${category.name}`}
        />
      </td>
      <td className={td}>
        <input value={form.description} onChange={(e) => setForm((x) => ({ ...x, description: e.target.value }))} className={input} aria-label={`Description of ${category.name}`} />
      </td>
      <td className={td}>
        <button
          type="button"
          role="switch"
          aria-checked={form.isActive}
          aria-label={`${category.name} active`}
          onClick={() => setForm((x) => ({ ...x, isActive: !x.isActive }))}
          className="flex items-center gap-2 font-mono text-[11px] uppercase tracking-[0.14em]"
        >
          <Pill band={statusBand(form.isActive ? "Active" : "Inactive")} label={form.isActive ? "Active" : "Hidden"} />
        </button>
      </td>
      <td className={td}>
        <span className="flex items-center gap-2">
          <Button variant="human" disabled={!dirty || busy} onClick={() => onSave({ ...form, slug: form.slug || slugify(form.name) })}>
            {dirty ? "Save" : "Saved"}
          </Button>
          <Button variant="ghost" disabled={busy} onClick={onDelete} aria-label={`Delete ${category.name}`}>
            <Trash2 size={12} aria-hidden />
          </Button>
        </span>
      </td>
    </tr>
  );
}
