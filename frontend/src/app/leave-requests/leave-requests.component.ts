import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import {
  CreateLeaveRequest,
  Employee,
  LeaveRequest,
  LeaveStatus,
  LeaveType,
  RowFeedback
} from '../models/leave-request.model';
import { EmployeesService } from '../services/employees.service';
import { LeaveRequestsService } from '../services/leave-requests.service';

function dateRangeValidator(group: AbstractControl): ValidationErrors | null {
  const start = group.get('startDate')?.value as string | null;
  const end = group.get('endDate')?.value as string | null;
  if (!start || !end) return null;
  if (start > end) return { dateRange: true };
  return null;
}

@Component({
  selector: 'app-leave-requests',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './leave-requests.component.html',
  styleUrls: ['./leave-requests.component.css']
})
export class LeaveRequestsComponent implements OnInit {
  readonly LeaveType = LeaveType;
  readonly LeaveStatus = LeaveStatus;

  requests: LeaveRequest[] = [];
  employees: Employee[] = [];
  loading = false;
  submitting = false;
  formError: string | null = null;
  formSuccess: string | null = null;

  approvingId: number | null = null;
  approveFeedback: RowFeedback | null = null;

  form: FormGroup;

  private readonly destroyRef = inject(DestroyRef);
  private readonly leaveRequests = inject(LeaveRequestsService);
  private readonly employeesApi = inject(EmployeesService);
  private readonly fb = inject(FormBuilder);

  constructor() {
    this.form = this.fb.group(
      {
        employeeId: [null as number | null, Validators.required],
        type: [null as LeaveType | null, Validators.required],
        startDate: ['', Validators.required],
        endDate: ['', Validators.required]
      },
      { validators: dateRangeValidator }
    );
  }

  ngOnInit(): void {
    this.loadRequests();
    this.employeesApi
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => (this.employees = data),
        error: () => (this.formError = 'Could not load employees.')
      });
  }

  get computedDays(): number | null {
    const start = this.form.get('startDate')?.value as string | null;
    const end = this.form.get('endDate')?.value as string | null;
    if (!start || !end) return null;
    const startMs = Date.parse(start);
    const endMs = Date.parse(end);
    if (Number.isNaN(startMs) || Number.isNaN(endMs)) return null;
    return Math.floor((endMs - startMs) / 86_400_000) + 1;
  }

  get daysInvalid(): boolean {
    const days = this.computedDays;
    return days !== null && days < 0;
  }

  loadRequests(): void {
    this.loading = true;
    this.leaveRequests
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.requests = data;
          this.loading = false;
        },
        error: () => {
          this.loading = false;
          this.formError = 'Could not load leave requests.';
        }
      });
  }

  submit(): void {
    this.formError = null;
    this.formSuccess = null;
    this.form.markAllAsTouched();

    if (this.form.invalid || this.daysInvalid) {
      this.formError = 'Please fix the form errors before submitting.';
      return;
    }

    const raw = this.form.getRawValue();
    const payload: CreateLeaveRequest = {
      employeeId: raw.employeeId as number,
      type: raw.type as LeaveType,
      startDate: raw.startDate as string,
      endDate: raw.endDate as string
    };

    this.submitting = true;
    this.leaveRequests
      .create(payload)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.submitting = false;
          this.formSuccess = 'Leave request submitted.';
          this.form.reset({
            employeeId: null,
            type: null,
            startDate: '',
            endDate: ''
          });
          this.loadRequests();
        },
        error: (err: HttpErrorResponse) => {
          this.submitting = false;
          this.formError = this.httpErrorMessage(err, 'Could not submit leave request.');
        }
      });
  }

  approve(id: number): void {
    if (this.approvingId !== null) return;

    this.approveFeedback = null;
    this.approvingId = id;

    this.leaveRequests
      .approve(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.approvingId = null;
          this.patchRequest(updated);
          this.approveFeedback = {
            id,
            kind: 'success',
            text: 'Request approved.'
          };
        },
        error: (err: HttpErrorResponse) => {
          this.approvingId = null;
          this.approveFeedback = {
            id,
            kind: 'error',
            text: this.approveErrorMessage(err)
          };
        }
      });
  }

  typeLabel(type: LeaveType): string {
    switch (type) {
      case LeaveType.Vacation:
        return 'Vacation';
      case LeaveType.Sick:
        return 'Sick';
      case LeaveType.Unpaid:
        return 'Unpaid';
      default:
        return 'Unknown';
    }
  }

  statusLabel(status: LeaveStatus): string {
    switch (status) {
      case LeaveStatus.Pending:
        return 'Pending';
      case LeaveStatus.Approved:
        return 'Approved';
      case LeaveStatus.Rejected:
        return 'Rejected';
      default:
        return 'Unknown';
    }
  }

  showError(controlName: string): boolean {
    const control = this.form.get(controlName);
    return !!control && control.invalid && control.touched;
  }

  private patchRequest(updated: LeaveRequest): void {
    const index = this.requests.findIndex((r) => r.id === updated.id);
    if (index < 0) return;

    const existing = this.requests[index];
    this.requests = [
      ...this.requests.slice(0, index),
      {
        ...existing,
        ...updated,
        employee: updated.employee ?? existing.employee
      },
      ...this.requests.slice(index + 1)
    ];
  }

  private approveErrorMessage(err: HttpErrorResponse): string {
    if (err.status === 409) {
      return this.httpErrorMessage(
        err,
        'This request was already approved or rejected.'
      );
    }
    if (err.status === 404) {
      return this.httpErrorMessage(err, 'Leave request not found.');
    }
    if (err.status === 400) {
      return this.httpErrorMessage(err, 'Cannot approve: not enough vacation balance.');
    }
    return this.httpErrorMessage(err, 'Could not approve leave request.');
  }

  private httpErrorMessage(err: HttpErrorResponse, fallback: string): string {
    return typeof err.error === 'string' && err.error.trim() ? err.error : fallback;
  }
}
