"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams, usePathname, useRouter } from "next/navigation";
import { Check, Minus, Plus, RotateCcw } from "lucide-react";
import { ProductCard } from "@/components/product-card";
import {
  BandChip,
  Button,
  ErrorNote,
  MagnitudeTick,
  PhotoPlate,
  Skeleton,
  StockDots,
} from "@/components/ui";
import { useAddToCart, useCart } from "@/features/cart";
import { useCategories, useProduct, useProducts } from "@/features/catalog";
import { specLabel } from "@/lib/attributes/registry";
import { asMember } from "@/lib/session";
import { ApiError, formatUsd, type Specs } from "@/infrastructure/api";
import { bandForCategory } from "@/lib/bands";
import { cn } from "@/lib/cn";

/** Format one spec value for the sheet: booleans read as yes or no, arrays join. */
function specValue(value: Specs[string]): string {
  if (value === null || value === undefined) return "—";
  if (typeof value === "boolean") return value ? "yes" : "no";
  if (Array.isArray(value)) return value.join(" · ");
  return String(value);
}

/** Product spec sheet with the full record: photo, decoded price, every spec. */
export default function ProductDetailPage() {
  const { slug } = useParams<{ slug: string }>();
  const router = useRouter();
  const pathname = usePathname();
  const { data: product, isPending, isError, refetch } = useProduct(slug);
  const { data: categories } = useCategories();
  const { data: cart } = useCart();
  const add = useAddToCart();
  const [qty, setQty] = useState(1);

  const onAdd = async () => {
    if (!(await asMember())) {
      router.push(`/login?next=${encodeURIComponent(pathname)}`);
      return;
    }
    add.mutate({ productId: product!.id, quantity: qty });
    setQty(1);
  };

  const category = categories?.find((c) => c.name === product?.categoryName);
  const band = bandForCategory(category?.slug ?? product?.categoryName?.toLowerCase());
  const inCart = cart?.items.find((l) => l.productId === product?.id);
  const soldOut = (product?.availableStock ?? 0) <= 0;

  const { data: siblings } = useProducts(
    { category: product?.categoryName ?? undefined, pageSize: 5 },
    { enabled: !!product?.categoryName }
  );
  const related = (siblings?.items ?? []).filter((p) => p.id !== product?.id).slice(0, 3);

  if (isPending) {
    return (
      <div className="mx-auto grid max-w-[1200px] gap-6 px-6 py-8 lg:grid-cols-2">
        <Skeleton className="h-[440px]" />
        <Skeleton className="h-[440px]" />
      </div>
    );
  }

  if (isError || !product) {
    return (
      <main className="mx-auto max-w-[720px] px-6 py-10">
        <ErrorNote>
          <strong>No unit on file with code &quot;{slug}&quot;.</strong> It may have been
          delisted. The bin wall always holds what is live.
          <div className="mt-3 flex gap-2">
            <Button variant="ghost" onClick={() => refetch()}>
              <RotateCcw size={13} aria-hidden /> Re-read sheet
            </Button>
            <Link href="/">
              <Button variant="ghost">Back to every bin</Button>
            </Link>
          </div>
        </ErrorNote>
      </main>
    );
  }

  const photo = product.images.find((i) => i.url)?.url || undefined;
  const specs = Object.entries(product.specs ?? {});

  return (
    <main className="mx-auto max-w-[1200px] px-6 py-8">
      {/* ------------------------------ breadcrumbs ----------------------------- */}
      <nav
        aria-label="Breadcrumb"
        className="flex flex-wrap items-center gap-2 font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft"
      >
        <Link href="/" className="hover:text-ink hover:underline">
          Every bin
        </Link>
        <span aria-hidden>/</span>
        {category ? (
          <Link href={`/category/${category.slug}`} className="flex items-center gap-1.5 hover:text-ink hover:underline">
            <BandChip band={band} size="sm" title={category.name} />
            {category.name}
          </Link>
        ) : (
          <span>{product.categoryName ?? "uncataloged"}</span>
        )}
        <span aria-hidden>/</span>
        <span className="text-ink">{product.sku}</span>
      </nav>

      <div className="mt-6 grid gap-10 lg:grid-cols-[1.05fr_1fr]">
        {/* --------------------------------- left -------------------------------- */}
        <section>
          <PhotoPlate sku={product.sku} band={band} name={product.name} image={photo} />

          {product.description && (
            <div className="mt-6 rounded-label border-[1.5px] border-line bg-card p-5">
              <h2 className="text-[11px] font-extrabold uppercase tracking-[0.26em] text-ink-soft">
                Bench notes
              </h2>
              <p className="mt-3 text-[14px] leading-relaxed">{product.description}</p>
              <p className="mt-3 border-t-[1.5px] border-dashed border-line pt-3 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
                Mock pricing for demo data only. Logged {product.createdAt.slice(0, 10)}.
              </p>
            </div>
          )}

          {related.length > 0 && (
            <section className="mt-10">
              <h2 className="text-[11px] font-extrabold uppercase tracking-[0.26em]">
                More from {product.categoryName}
              </h2>
              <div className="mt-4 grid gap-6 md:grid-cols-3">
                {related.map((p) => (
                  <ProductCard key={p.id} product={p} />
                ))}
              </div>
            </section>
          )}
        </section>

        {/* -------------------------------- right -------------------------------- */}
        <section>
          <p className="font-mono text-[11px] uppercase tracking-[0.2em] text-ink-soft">
            {product.brand ?? "unbranded"} · unit {product.sku}
          </p>
          <h1 className="mt-2 text-3xl font-extrabold uppercase leading-tight tracking-[0.02em]">
            {product.name}
          </h1>

          <div className="mt-5 rounded-label border-[1.5px] border-ink bg-card p-5">
            <div className="flex items-end justify-between gap-4">
              <div>
                <div className="flex items-center gap-3">
                  <MagnitudeTick value={product.price} className="h-7 w-[7px]" />
                  <span className="font-mono text-[30px] font-bold leading-none tabular-nums">
                    {formatUsd(product.price)}
                  </span>
                </div>
              </div>
              <StockDots qty={product.availableStock} />
            </div>
            <p className="mt-3 border-t-[1.5px] border-dashed border-line pt-3 text-[12px] text-ink-soft">
              {soldOut
                ? "Bin empty. Restock ETA unknown. The assistant cannot order what is not on the shelf."
                : `${product.availableStock} unit${product.availableStock === 1 ? "" : "s"} physically in this bin. A quote reserves them; nobody pays until you do on the hosted page.`}
            </p>

            {!soldOut && (
              <div className="mt-4 flex flex-wrap items-center gap-3">
                <div className="flex items-center rounded-[4px] border-[1.5px] border-ink">
                  <button
                    aria-label="One fewer"
                    onClick={() => setQty((q) => Math.max(1, q - 1))}
                    className="px-2.5 py-2 hover:bg-ink/10 disabled:opacity-40"
                    disabled={qty <= 1}
                  >
                    <Minus size={13} aria-hidden />
                  </button>
                  <span className="w-8 text-center font-mono text-[14px] tabular-nums">{qty}</span>
                  <button
                    aria-label="One more"
                    onClick={() => setQty((q) => Math.min(product.availableStock, q + 1))}
                    className="px-2.5 py-2 hover:bg-ink/10 disabled:opacity-40"
                    disabled={qty >= product.availableStock}
                  >
                    <Plus size={13} aria-hidden />
                  </button>
                </div>

                <Button
                  variant="add"
                  disabled={add.isPending}
                  onClick={onAdd}
                >
                  <Check size={13} aria-hidden />
                  {add.isPending ? "Filing…" : `Add ${qty > 1 ? `${qty} ` : "one "}to cart`}
                </Button>

                {add.isSuccess && (
                  <Link
                    href="/quote"
                    className="font-mono text-[11px] uppercase tracking-[0.14em] text-band-green underline underline-offset-4"
                  >
                    Filed. Open the bill
                  </Link>
                )}
              </div>
            )}

            {inCart && (
              <p className="mt-3 font-mono text-[11px] uppercase tracking-[0.12em] text-ink-soft">
                Already in your cart × {inCart.quantity}
              </p>
            )}

            {add.error && (
              <p role="alert" className="mt-3 font-mono text-[11px] uppercase tracking-[0.1em] text-band-red">
                {add.error instanceof ApiError ? add.error.message : "Could not file the line."}
              </p>
            )}
          </div>

          {/* yellow marks a human-only hand-off. This link invites the assistant; it never acts. */}
          <Link
            href={`/assistant?cat=${category?.slug ?? ""}&q=${encodeURIComponent(product.name)}`}
            className={cn(
              "mt-4 flex items-center justify-between rounded-label border-[1.5px] border-ink bg-band-yellow px-4 py-3",
              "font-extrabold uppercase tracking-[0.08em] text-[#1a1a1a] hover:brightness-95"
            )}
          >
            <span>Ask the assistant to vet this</span>
            <span aria-hidden className="font-mono text-[13px]">→</span>
          </Link>

          {/* ------------------------------ spec sheet ----------------------------- */}
          <h2 className="mt-8 text-[11px] font-extrabold uppercase tracking-[0.26em]">
            Full specification
          </h2>
          <table className="mt-3 w-full border-collapse text-[13px]">
            <tbody>
              <tr className="border-y border-line/50">
                <th className="w-[130px] bg-kraft/60 px-3 py-2 text-left align-top font-mono text-[11px] font-bold uppercase tracking-[0.14em] text-ink-soft">
                  SKU
                </th>
                <td className="px-3 py-2 font-mono">{product.sku}</td>
              </tr>
              <tr className="border-b border-line/50">
                <th className="px-3 py-2 text-left align-top font-mono text-[11px] font-bold uppercase tracking-[0.14em] text-ink-soft">
                  Status
                </th>
                <td className="px-3 py-2">{product.status}</td>
              </tr>
              {specs.map(([key, value]) => (
                <tr key={key} className="border-b border-line/50">
                  <th className="bg-kraft/60 px-3 py-2 text-left align-top font-mono text-[11px] font-bold uppercase tracking-[0.14em] text-ink-soft">
                    {specLabel(key)}
                  </th>
                  <td className="px-3 py-2">{specValue(value)}</td>
                </tr>
              ))}
              {specs.length === 0 && (
                <tr className="border-b border-line/50">
                  <td colSpan={2} className="px-3 py-3 text-ink-soft">
                    No attributes recorded for this unit yet.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </section>
      </div>
    </main>
  );
}
