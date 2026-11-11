"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useForm } from "react-hook-form";
import { UserPlus } from "lucide-react";
import { z } from "zod";
import { Button, ErrorNote, Skeleton } from "@/components/ui";
import { ApiError } from "@/lib/api/types";
import { useSession } from "@/lib/session";
import { authInput, authLabel, safeNext } from "@/app/login/page";

const registerSchema = z.object({
  firstName: z.string().min(1, "First name is required."),
  lastName: z.string().min(1, "Last name is required."),
  email: z.email("Enter a valid email address."),
  password: z.string().min(8, "Password must be at least 8 characters."),
  confirm: z.string(),
  phoneNumber: z.string().optional(),
}).refine((v) => v.password === v.confirm, {
  message: "Passwords do not match.",
  path: ["confirm"],
});
type RegisterValues = z.infer<typeof registerSchema>;

function FieldError({ children }: { children: React.ReactNode }) {
  return (
    <span role="alert" className="mt-1 block font-mono text-[11px] text-band-red">
      {children}
    </span>
  );
}

function RegisterInner() {
  const router = useRouter();
  const params = useSearchParams();
  const next = safeNext(params?.get("next"), "/account");
  const register = useSession((s) => s.register);
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register: field,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterValues>({ mode: "onTouched" });

  const submit = handleSubmit(async (values) => {
    setServerError(null);
    const parsed = registerSchema.safeParse(values);
    if (!parsed.success) {
      for (const issue of parsed.error.issues) {
        setError(issue.path[0] as keyof RegisterValues, { message: issue.message });
      }
      return;
    }
    try {
      await register({
        email: parsed.data.email,
        password: parsed.data.password,
        firstName: parsed.data.firstName,
        lastName: parsed.data.lastName,
        phoneNumber: parsed.data.phoneNumber?.trim() || null,
      });
      router.push(next);
    } catch (e) {
      if (e instanceof ApiError) {
        for (const [fieldName, msgs] of Object.entries(e.errors ?? {})) {
          if (fieldName) setError(fieldName.toLowerCase() as keyof RegisterValues, { message: msgs[0] });
        }
        setServerError(e.title);
      } else setServerError("Could not reach the shop. Try again.");
    }
  });

  return (
    <main className="mx-auto flex min-h-[calc(100vh-57px)] max-w-[460px] flex-col justify-center px-6 py-10">
      <p className="font-mono text-[11px] uppercase tracking-[0.22em] text-ink-soft">shopmind · new card</p>
      <h1 className="mt-2 text-2xl font-extrabold uppercase tracking-[0.04em]">Create account</h1>
      <div className="mt-6 rounded-label border-[1.5px] border-ink bg-card p-5">
        <form onSubmit={submit} noValidate className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <label>
              <span className={authLabel}>First name</span>
              <input autoComplete="given-name" {...field("firstName")} className={authInput} />
              {errors.firstName && <FieldError>{errors.firstName.message}</FieldError>}
            </label>
            <label>
              <span className={authLabel}>Last name</span>
              <input autoComplete="family-name" {...field("lastName")} className={authInput} />
              {errors.lastName && <FieldError>{errors.lastName.message}</FieldError>}
            </label>
          </div>
          <label>
            <span className={authLabel}>Email</span>
            <input type="email" autoComplete="email" {...field("email")} className={authInput} placeholder="you@example.com" />
            {errors.email && <FieldError>{errors.email.message}</FieldError>}
          </label>
          <label>
            <span className={authLabel}>Phone (optional)</span>
            <input autoComplete="tel" {...field("phoneNumber")} className={authInput} placeholder="+1-555-…" />
          </label>
          <div className="grid grid-cols-2 gap-3">
            <label>
              <span className={authLabel}>Password</span>
              <input type="password" autoComplete="new-password" {...field("password")} className={authInput} />
              {errors.password && <FieldError>{errors.password.message}</FieldError>}
            </label>
            <label>
              <span className={authLabel}>Confirm</span>
              <input type="password" autoComplete="new-password" {...field("confirm")} className={authInput} />
              {errors.confirm && <FieldError>{errors.confirm.message}</FieldError>}
            </label>
          </div>

          {serverError && (
            <ErrorNote>
              <strong>{serverError}</strong>{" "}
              {serverError.includes("registered") ? "Try signing in instead." : ""}
            </ErrorNote>
          )}

          <Button type="submit" variant="human" className="w-full justify-center" disabled={isSubmitting}>
            <UserPlus size={13} aria-hidden />
            {isSubmitting ? "Filing…" : "Create account & sign in"}
          </Button>
          <p className="text-center font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
            already keyed in?{" "}
            <Link href="/login" className="text-ink underline underline-offset-4">
              sign in
            </Link>
          </p>
        </form>
      </div>
      <p className="mt-4 font-mono text-[11px] uppercase leading-relaxed tracking-[0.12em] text-ink-soft">
        accounts persist in this browser&apos;s fake-server state until demo reset.
        The real build posts the same RegisterRequest to Shop.Api.
      </p>
    </main>
  );
}

export default function RegisterPage() {
  return (
    <Suspense fallback={<Skeleton className="mx-auto mt-6 h-64 max-w-[460px]" />}>
      <RegisterInner />
    </Suspense>
  );
}
