"use client";

import { CatalogView } from "@/components/catalog-view";

/** The home page renders the full bin wall: rail, facets, pagination, and the assistant drawer. */
export default function CatalogPage() {
  return <CatalogView bins assistant />;
}
