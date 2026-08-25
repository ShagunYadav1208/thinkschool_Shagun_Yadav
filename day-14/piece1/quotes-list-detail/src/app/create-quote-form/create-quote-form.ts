import { Component, ElementRef, EventEmitter, Output, ViewChild, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { QuotesService } from '../quotes.service';
import { Quote } from '../models/quote.model';

const LIMITS = { author: 100, text: 1000 } as const;

@Component({
  selector: 'app-create-quote-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote-form.html',
  styleUrl: './create-quote-form.css',
})
export class CreateQuoteForm {
  private readonly fb = inject(FormBuilder);
  private readonly quotesService = inject(QuotesService);

  @ViewChild('authorInput') private authorInputRef?: ElementRef<HTMLInputElement>;
  @ViewChild('textInput') private textInputRef?: ElementRef<HTMLTextAreaElement>;

  @Output() created = new EventEmitter<Quote>();

  // Limits match CreateQuoteRequest.cs / the live 400 responses exactly:
  // author max 100, text max 1000, both required.
  protected readonly form = this.fb.nonNullable.group({
    author: ['', [Validators.required, Validators.maxLength(LIMITS.author)]],
    text: ['', [Validators.required, Validators.maxLength(LIMITS.text)]],
  });

  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);

  protected readonly limits = LIMITS;

  // Live character counts, driven straight off the controls' own valueChanges
  // (not a separate `(input)` handler) so pasted text and programmatic
  // resets stay in sync with the counter too.
  private readonly authorValue = toSignal(this.form.controls.author.valueChanges, {
    initialValue: this.form.controls.author.value,
  });
  private readonly textValue = toSignal(this.form.controls.text.valueChanges, {
    initialValue: this.form.controls.text.value,
  });

  protected charCount(field: 'author' | 'text'): number {
    return (field === 'author' ? this.authorValue() : this.textValue()).length;
  }

  protected isNearLimit(field: 'author' | 'text'): boolean {
    return this.charCount(field) >= this.limits[field];
  }

  protected isInvalid(field: 'author' | 'text'): boolean {
    const control = this.form.get(field)!;
    return control.invalid && (control.touched || control.dirty);
  }

  protected errorMessage(field: 'author' | 'text'): string {
    const control = this.form.get(field)!;
    if (control.hasError('server')) return control.getError('server');
    if (control.hasError('required')) return `${field === 'author' ? 'Author' : 'Text'} is required.`;
    if (control.hasError('maxlength')) {
      const limit = control.getError('maxlength').requiredLength;
      return `${field === 'author' ? 'Author' : 'Text'} must be ${limit} characters or fewer.`;
    }
    return '';
  }

  protected onSubmit(): void {
    this.serverError.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidControl();
      return;
    }

    this.submitting.set(true);
    this.quotesService.createQuote(this.form.getRawValue()).subscribe({
      next: (quote) => {
        this.submitting.set(false);
        this.form.reset({ author: '', text: '' });
        this.created.emit(quote);
      },
      error: (err) => {
        this.submitting.set(false);
        if (err.status === 400 && err.error?.errors) {
          const errors = err.error.errors as Record<string, string[]>;
          for (const [field, messages] of Object.entries(errors)) {
            this.form.get(field)?.setErrors({ server: messages[0] });
          }
          this.focusFirstInvalidControl();
        } else {
          this.serverError.set("Couldn't add the quote. Please try again.");
        }
      },
    });
  }

  // The first draft only called markAllAsTouched() - that makes the errors
  // VISIBLE (aria-invalid/aria-describedby update), but never moves
  // keyboard/screen-reader focus there. Confirmed missing with a real
  // Playwright check (document.activeElement stayed on the submit button
  // after a failed submit) before this existed.
  //
  // The first fix attempt queried the DOM for `[aria-invalid="true"]`
  // inside a `queueMicrotask`, assuming that would run after Angular
  // re-rendered the aria-invalid binding. It didn't: this is a ZONELESS
  // app, so a plain FormGroup state change doesn't get flushed to the DOM
  // on the same microtask tick the way it would under Zone.js - the
  // queryless focus() call ran before aria-invalid ever got set, and the
  // re-verified Playwright check still showed the submit button focused.
  // Checking the FormGroup's own validity directly (no DOM/render-timing
  // dependency at all) and focusing a stable ViewChild reference sidesteps
  // the whole question of when the DOM catches up.
  private focusFirstInvalidControl(): void {
    if (this.form.controls.author.invalid) {
      this.authorInputRef?.nativeElement.focus();
    } else if (this.form.controls.text.invalid) {
      this.textInputRef?.nativeElement.focus();
    }
  }
}
