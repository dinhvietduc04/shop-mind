"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { LogOut, MapPin, Package } from "lucide-react";
import { RequireAuth } from "@/components/guard";
import { BandChip, Button, ErrorNote, Skeleton } from "@/components/ui";
import { useAccountAddress, useSaveAddress } from "@/lib/api/hooks";
import { DEMO_ADDRESS } from "@/lib/quotes/intake";
import { ApiError, type ShippingAddressDto } from "@/lib/api/types";
import { useSession } from "@/lib/session";

function AccountInner() {
  const { user, logout } = useSession();
  const router = useRouter();
  const { data, isPending } = useAccountAddress();

  return (
    <main className="mx-auto max-w-[680px] px-6 py-6">
      <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">My account</h1>

      {/* profile */}
      <section className="mt-6 rounded-label border-[1.5px] border-ink bg-card p-5">
        <div className="flex flex-wrap items-center gap-3">
          <span className="grid h-11 w-11 place-items-center rounded-full border-[1.5px] border-ink bg-kraft font-mono text-[15px] font-bold">
            {user?.firstName?.[0] ?? "·"}
            {user?.lastName?.[0] ?? ""}
          </span>
          <div className="min-w-0">
            <p className="text-[15px] font-extrabold uppercase tracking-[0.03em]">
              {user?.firstName} {user?.lastName}
            </p>
            <p className="font-mono text-[12px] text-ink-soft">{user?.email}</p>
          </div>
          <span className="ml-auto flex items-center gap-2 font-mono text-[11px] uppercase tracking-[0.18em] text-ink-soft">
            <BandChip band={user?.role === "Admin" ? "red" : "green"} size="sm" />
            role · {user?.role}
          </span>
        </div>
        {user?.phoneNumber && (
          <p className="mt-3 font-mono text-[11px] text-ink-soft">phone · {user.phoneNumber}</p>
        )}
        <div className="mt-4 flex flex-wrap gap-2">
          <Link href="/orders">
            <Button variant="ghost">
              <Package size={13} aria-hidden /> Order ledger
            </Button>
          </Link>
          <Button
            variant="ghost"
            onClick={() => {
              logout();
              router.push("/");
            }}
          >
            <LogOut size={13} aria-hidden /> Sign out
          </Button>
        </div>
      </section>

      {/* shipping preset */}
      <section className="mt-6 rounded-label border-[1.5px] border-ink bg-card p-5">
        <h2 className="flex items-center gap-2 text-[11px] font-extrabold uppercase tracking-[0.26em]">
          <MapPin size={13} aria-hidden /> Shipping preset
        </h2>
        <p className="mt-2 text-[13px] leading-relaxed text-ink-soft">
          Quotes are reserved for this address. The bill shows it verbatim. The
          agent quotes to it as-is; changing it here is a human edit.
        </p>

        {isPending ? (
          <Skeleton className="mt-4 h-40" />
        ) : (
          <ShippingForm initial={data?.address ?? DEMO_ADDRESS} />
        )}
      </section>
    </main>
  );
}

/**
 * Mounts only once the preset query has settled, so useState starts from the
 * server's value. No effect syncs state after hydration
 * (react-hooks/set-state-in-effect).
 */
function ShippingForm({ initial }: { initial: ShippingAddressDto }) {
  const save = useSaveAddress();
  const [form, setForm] = useState<ShippingAddressDto>(initial);
  const input =
    "mt-1 w-full rounded-[4px] border-[1.5px] border-line bg-paper px-3 py-2 font-mono text-[13px] focus:border-ink";
  const label = "block font-mono text-[11px] font-bold uppercase tracking-[0.18em] text-ink-soft";
  const set = (k: keyof ShippingAddressDto) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  return (
    <>
      <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
        <label>
          <span className={label}>Full name</span>
          <input name="fullName" value={form.fullName} onChange={set("fullName")} className={input} />
        </label>
        <label>
          <span className={label}>Phone</span>
          <input name="phone" value={form.phone} onChange={set("phone")} className={input} />
        </label>
        <label>
          <span className={label}>Address</span>
          <input name="address" value={form.address} onChange={set("address")} className={input} />
        </label>
        <label>
          <span className={label}>City</span>
          <input name="city" value={form.city} onChange={set("city")} className={input} />
        </label>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-3">
        <Button variant="add" disabled={save.isPending} onClick={() => save.mutate(form)}>
          {save.isPending ? "Saving…" : "Save preset"}
        </Button>
        {save.isSuccess && (
          <span className="font-mono text-[11px] uppercase tracking-[0.14em] text-band-green">
            saved, so the next quote uses it
          </span>
        )}
      </div>
      {save.error && (
        <div className="mt-3">
          <ErrorNote>
            {save.error instanceof ApiError ? save.error.title : "Could not save the address."}
          </ErrorNote>
        </div>
      )}
    </>
  );
}

export default function AccountPage() {
  return (
    <RequireAuth>
      <AccountInner />
    </RequireAuth>
  );
}
