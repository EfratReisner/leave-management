import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { Employee, LeaveRequest } from '../models/leave-request.model';

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
  requests: LeaveRequest[] = [];
  employees: Employee[] = [];
  loading = false;
  submitting = false;
  formError: string | null = null;
  formSuccess: string | null = null;

  private readonly leaveApiUrl = 'http://localhost:5080/api/leave-requests';
  private readonly employeesApiUrl = 'http://localhost:5080/api/employees';

  form = this.fb.group(
    {
      employeeId: [null as number | null, Validators.required],
      type: [null as number | null, Validators.required],
      startDate: ['', Validators.required],
      endDate: ['', Validators.required]
    },
    { validators: dateRangeValidator }
  );

  constructor(
    private http: HttpClient,
    private fb: FormBuilder
  ) {}

  ngOnInit(): void {
    this.load();
    this.http.get<Employee[]>(this.employeesApiUrl).subscribe({
      next: (data) => (this.employees = data),
      error: () => (this.formError = 'Could not load employees.')
    });
  }

  get computedDays(): number | null {
    const start = this.form.get('startDate')?.value;
    const end = this.form.get('endDate')?.value;
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

  load(): void {
    this.loading = true;
    this.http.get<LeaveRequest[]>(this.leaveApiUrl).subscribe({
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

    const { employeeId, type, startDate, endDate } = this.form.getRawValue();
    this.submitting = true;

    this.http
      .post<LeaveRequest>(this.leaveApiUrl, {
        employeeId,
        type,
        startDate,
        endDate
      })
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
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.submitting = false;
          this.formError =
            typeof err.error === 'string' && err.error.trim()
              ? err.error
              : 'Could not submit leave request.';
        }
      });
  }

  approve(id: number): void {
    this.http.post(`${this.leaveApiUrl}/${id}/approve`, {}).subscribe(() => {
      this.load();
    });
  }

  typeLabel(type: number): string {
    if (type === 0) return 'Vacation';
    if (type === 1) return 'Sick';
    return 'Unpaid';
  }

  statusLabel(status: number): string {
    if (status === 0) return 'Pending';
    if (status === 1) return 'Approved';
    return 'Rejected';
  }

  showError(controlName: string): boolean {
    const control = this.form.get(controlName);
    return !!control && control.invalid && control.touched;
  }
}
