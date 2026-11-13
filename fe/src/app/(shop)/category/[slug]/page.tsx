"use client";

import { useParams } from "next/navigation";
import Link from "next/link";
import { BinChips, CatalogView } from "@/components/catalog-view";
import { Button, ErrorNote, Skeleton } from "@/components/ui";
import { useCategories } from "@/features/catalog";
import { bandClass, bandForCategory } from "@/lib/bands";
import { cn } from "@/lib/cn";

/** Category landing with the catalog pinned to one bin. */
export default function CategoryPage() {
  const { slug } = useParams<{ slug: string }>();
  const { data: categories, isPending } = useCategories();
  const category = categories?.find((c) => c.slug === slug);

  if (isPending) return <Skeleton className="mx-6 mt-6 h-64" />;

  if (!category) {
    return (
      <main className="mx-auto max-w-[720px] px-6 py-10">
        <ErrorNote>
          <strong>No bin named &quot;{slug}&quot;.</strong> The catalog holds eight bins.
          Pick one from the wall.
          <div className="mt-3">
            <Link href="/">
              <Button variant="ghost">Back to every bin</Button>
            </Link>
          </div>
        </ErrorNote>
      </main>
    );
  }

  const band = bandForCategory(category.slug);
  return (
    <CatalogView
      lockedCategory={category.slug}
      headerExtra={
        <>
          <p className="mt-3 max-w-[60ch] text-[13px] leading-relaxed text-ink-soft">
            <span
              aria-hidden
              className={cn(
                "mr-2 inline-block h-3.5 w-2.5 translate-y-0.5 rounded-[2px] border-[1.5px] border-[#1a1a1a] dark:border-[#8a8068]",
                bandClass(band)
              )}
            />
            {category.description ?? "A bin on the ShopMind wall."}
          </p>
          <BinChips activeSlug={category.slug} />
        </>
      }
    />
  );
}
