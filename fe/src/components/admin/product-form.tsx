"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, Plus, X } from "lucide-react";
import { Panel } from "@/components/admin/widgets";
import { BandChip, Button, ErrorNote, MagnitudeTick, StockDots } from "@/components/ui";
import { useAdminCategories } from "@/lib/api/hooks";
import { saveProduct } from "@/lib/admin/product-intake";
import { specsSchema } from "@/lib/admin/product-schema";
import { formatUsd, type ProductAdminDto, type Specs } from "@/lib/api/types";
import { bandForCategory, BAND_BG } from "@/lib/bands";
import { cn } from "@/lib/cn";

/* Field legality is stated once in lib/admin/product-schema and enforced
   twice: the intake validates locally for instant feedback, and the mock
   server (same schema) answers any write that skipped the form. */

const FIELD_KEYS = [
  "name",
  "slug",
  "sku",
  "brand",
  "categoryId",
  "status",
  "price",
  "description",
  "specs",
  "initialStock",
] as const;
type FieldKey = (typeof FIELD_KEYS)[number];
type FieldErrors = Partial<Record<FieldKey, string>>;
const FIELD_KEY_SET = new Set<string>(FIELD_KEYS);

const input =
  "mt-1 w-full rounded-[4px] border-[1.5px] border-line bg-paper px-3 py-2 font-mono text-[13px] focus:border-ink";
const label = "block font-mono text-[11px] font-bold uppercase tracking-[0.18em] text-ink-soft";

interface ImageRow {
  url: string;
  altText: string;
}

export function ProductForm({ product }: { product?: ProductAdminDto }) {
  const router = useRouter();
  const isEdit = Boolean(product);
  const { data: categories } = useAdminCategories();
  const [pending, setPending] = useState(false);

  const [f, setF] = useState({
    name: product?.name ?? "",
    slug: product?.slug ?? "",
    sku: product?.sku ?? "",
    brand: product?.brand ?? "",
    categoryId: product?.categoryId ?? "",
    status: product?.status ?? ("Active" as const),
    price: String(product?.price ?? ""),
    initialStock: String(product?.quantity ?? 0),
    description: product?.description ?? "",
  });
  const [specsText, setSpecsText] = useState(
    product?.specs ? JSON.stringify(product.specs, null, 2) : ""
  );
  const [images, setImages] = useState<ImageRow[]>(
    product?.images.filter((i) => i.url).map((i) => ({ url: i.url, altText: i.altText ?? "" })) ?? []
  );
  const [errs, setErrs] = useState<FieldErrors>({});
  const [serverNote, setServerNote] = useState<string | null>(null);

  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) =>
    setF((prev) => ({ ...prev, [k]: e.target.value }));

  const priceNum = Number(f.price);
  const priceOk = Number.isFinite(priceNum) && priceNum > 0;
  const catBand = BAND_BG[bandForCategory(categories?.find((c) => c.id === f.categoryId)?.slug)];

  /** Maps the server and intake problem map to local field errors. Keys already speak the
   *  backend's camelCase; anything unrecognised becomes an honest note. */
  const applyProblems = (problems: Record<string, string[]>, title: string) => {
    const next: FieldErrors = {};
    let note: string | null = null;
    for (const [key, msgs] of Object.entries(problems)) {
      if (FIELD_KEY_SET.has(key)) next[key as FieldKey] = msgs[0];
      else note = msgs[0] ?? note;
    }
    setErrs(next);
    setServerNote(note ?? (Object.keys(problems).length ? null : title));
  };

  const submit = async () => {
    setServerNote(null);
    const nextErrs: FieldErrors = {};

    let specs: Specs | undefined;
    if (specsText.trim()) {
      try {
        const rawSpecs = JSON.parse(specsText) as unknown;
        const sp = specsSchema.safeParse(rawSpecs);
        if (!sp.success) nextErrs.specs = sp.error.issues[0]?.message ?? "Specs must be a flat JSON object of scalars.";
        else specs = sp.data as Specs;
      } catch {
        nextErrs.specs = "Not valid JSON. The sheet must parse as an object.";
      }
    } else specs = isEdit ? product?.specs : {};

    setErrs(nextErrs);
    if (Object.keys(nextErrs).length > 0) return;

    const body = {
      name: f.name,
      slug: f.slug || null,
      sku: f.sku.toUpperCase(),
      categoryId: f.categoryId,
      description: f.description || null,
      brand: f.brand || null,
      price: priceNum,
      status: f.status,
      images: images
        .filter((i) => i.url.trim())
        .map((i, n) => ({ url: i.url.trim(), altText: i.altText.trim() || null, displayOrder: n })),
      specs,
    };

    setPending(true);
    const out = await saveProduct({
      productId: product?.id,
      body,
      stock: Number(f.initialStock),
      previousStock: product?.quantity,
    });
    setPending(false);

    if (!out.ok) {
      if ("network" in out) setServerNote("Could not reach the shop.");
      else applyProblems(out.problems, out.title);
      return;
    }
    if ("stockWarning" in out) {
      // partial success, stated: the sheet saved, the stock row did not move
      setServerNote(`${out.stockWarning} The product saved, only stock is stale. Retry the stock number.`);
      return;
    }
    router.push("/admin/products");
  };

  return (
    <div className="mt-5 space-y-5">
      <Link href="/admin/products" className="inline-flex items-center gap-1.5 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft hover:text-ink">
        <ArrowLeft size={12} aria-hidden /> back to catalog
      </Link>

      <Panel title={isEdit ? `Edit · ${product?.name}` : "New product"}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <label>
            <span className={label}>Name *</span>
            <input name="productName" value={f.name} onChange={set("name")} className={input} />
            <FieldErr msg={errs.name} />
          </label>
          <label>
            <span className={label}>Slug (auto from name if blank)</span>
            <input name="productSlug" value={f.slug} onChange={set("slug")} className={input} placeholder="thinkpad-x1-carbon-gen-11" />
            <FieldErr msg={errs.slug} />
          </label>
          <label>
            <span className={label}>SKU *</span>
            <input name="productSku" value={f.sku} onChange={set("sku")} className={cn(input, "uppercase")} placeholder="LEN-X1C-G11" />
            <FieldErr msg={errs.sku} />
          </label>
          <label>
            <span className={label}>Brand</span>
            <input name="productBrand" value={f.brand} onChange={set("brand")} className={input} />
          </label>
          <label>
            <span className={label}>Category *</span>
            <select name="productCategory" value={f.categoryId} onChange={set("categoryId")} className={input}>
              <option value="">pick a bin</option>
              {categories?.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
            <FieldErr msg={errs.categoryId} />
          </label>
          <label>
            <span className={label}>Status</span>
            <select name="productStatus" value={f.status} onChange={set("status")} className={input}>
              <option>Active</option>
              <option>Draft</option>
              <option>Inactive</option>
            </select>
          </label>
          <label>
            <span className={label}>Price (USD) *</span>
            <input name="productPrice" value={f.price} onChange={set("price")} inputMode="decimal" className={input} />
            <FieldErr msg={errs.price} />
          </label>
          <label>
            <span className={label}>{isEdit ? "Total stock (reserved is protected)" : "Initial stock *"}</span>
            <input name="productStock" value={f.initialStock} onChange={set("initialStock")} inputMode="numeric" className={input} />
            <FieldErr msg={errs.initialStock} />
          </label>
          <label className="sm:col-span-2">
            <span className={label}>Description</span>
            <textarea name="productDescription" value={f.description} onChange={set("description")} rows={2} className={input} />
          </label>
        </div>

        {/* live band preview of the label the customer will actually read */}
        <div className="mt-4 rounded-[4px] border-[1.5px] border-dashed border-line px-4 py-3">
          <p className="font-mono text-[11px] uppercase tracking-[0.18em] text-ink-soft">label preview, as the storefront renders it</p>
          <div className="mt-2 flex flex-wrap items-center gap-3">
            <span className={`h-4 w-4 rounded-[3px] border-[1.5px] border-[#1a1a1a] ${catBand}`} aria-hidden title="category band" />
            {priceOk ? <MagnitudeTick value={priceNum} className="h-5 w-[6px]" /> : <span className="font-mono text-[11px] text-ink-soft">price…</span>}
            {priceOk && <span className="font-mono text-[13px] font-bold">{formatUsd(priceNum)}</span>}
            <StockDots qty={Number(f.initialStock) || 0} />
            <span className="ml-auto flex items-center gap-1.5 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
              <BandChip band={f.status === "Active" ? "green" : f.status === "Draft" ? "yellow" : "red"} size="sm" />
              {f.status}
            </span>
          </div>
        </div>

        {/* images */}
        <h3 className="mt-5 font-mono text-[11px] font-bold uppercase tracking-[0.22em]">Photos</h3>
        <p className="mt-1 text-[11px] text-ink-soft">
          URL paths under <span className="font-mono">/products/</span> (see public/products/PROVENANCE.md) or absolute https.
        </p>
        <div className="mt-2 space-y-2">
          {images.map((img, n) => (
            <div key={n} className="flex items-center gap-2">
              <input
                value={img.url}
                onChange={(e) => setImages((xs) => xs.map((x, i) => (i === n ? { ...x, url: e.target.value } : x)))}
                placeholder="/products/thing.jpg"
                className={cn(input, "mt-0 flex-1")}
                aria-label={`Image ${n + 1} URL`}
              />
              <input
                value={img.altText}
                onChange={(e) => setImages((xs) => xs.map((x, i) => (i === n ? { ...x, altText: e.target.value } : x)))}
                placeholder="alt text"
                className={cn(input, "mt-0 w-40")}
                aria-label={`Image ${n + 1} alt text`}
              />
              <Button variant="ghost" aria-label={`Remove image ${n + 1}`} onClick={() => setImages((xs) => xs.filter((_, i) => i !== n))}>
                <X size={12} aria-hidden />
              </Button>
            </div>
          ))}
          <Button variant="ghost" onClick={() => setImages((xs) => [...xs, { url: "", altText: "" }])}>
            <Plus size={12} aria-hidden /> Add photo row
          </Button>
        </div>

        {/* specs editor. A3 requires flat scalars; keys are the facet vocabulary */}
        <h3 className="mt-5 flex items-center gap-2 font-mono text-[11px] font-bold uppercase tracking-[0.22em]">
          Specs JSON <span className="font-normal text-ink-soft">· feeds facets &amp; the assistant&apos;s questions</span>
        </h3>
        <textarea
          name="productSpecs"
          value={specsText}
          onChange={(e) => setSpecsText(e.target.value)}
          rows={6}
          spellCheck={false}
          placeholder='{"ram_gb": 16, "storage_gb": 512, "wireless": true}'
          aria-label="Specs JSON editor"
          className={cn(input, "min-h-[120px] font-mono text-[12px] leading-relaxed")}
        />
        <FieldErr msg={errs.specs} />

        {serverNote && (
          <div className="mt-3">
            <ErrorNote><strong>{serverNote}</strong></ErrorNote>
          </div>
        )}

        <div className="mt-5 flex gap-2">
          <Button variant="human" onClick={submit} disabled={pending}>
            {pending ? "Filing…" : isEdit ? "Save product" : "Create product"}
          </Button>
          <Link href="/admin/products">
            <Button variant="ghost">Cancel</Button>
          </Link>
        </div>
      </Panel>
    </div>
  );
}

function FieldErr({ msg }: { msg?: string }) {
  if (!msg) return null;
  return (
    <span role="alert" className="mt-1 block font-mono text-[11px] text-band-red">
      {msg}
    </span>
  );
}
