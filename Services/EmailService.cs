using System.Net;
using System.Net.Mail;

namespace DailyTrackerAPI.Services
{
    public interface IEmailService
    {
        Task SendOtpEmailAsync(string email, string code, string purpose);
        Task SendWelcomeEmailAsync(string email, string fullName);
    }
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendOtpEmailAsync(string email, string code, string purpose)
        {
            var subject = $"Your OTP for {purpose}";
            var body = $@"
            <!DOCTYPE html>
            <html>
            <body style='margin:0;padding:0;background-color:#f4f6f9;font-family:Arial,sans-serif;'>

                <table width='100%' cellpadding='0' cellspacing='0' style='padding:20px 0;'>
                    <tr>
                        <td align='center'>
                    
                            <table width='600' cellpadding='0' cellspacing='0' 
                                   style='background:#ffffff;border-radius:8px;padding:30px;'>

                                <tr>
                                    <td align='center' style='padding-bottom:20px;'>
                                        <h2 style='color:#2c3e50;margin:0;'>
                                            Employee Management System
                                        </h2>
                                    </td>
                                </tr>

                                <tr>
                                    <td style='font-size:16px;color:#333;'>
                                        <p>Hello,</p>
                                        <p>You requested an OTP for <strong>{purpose}</strong>.</p>
                                    </td>
                                </tr>

                                <tr>
                                    <td align='center' style='padding:20px 0;'>
                                        <div style='
                                            display:inline-block;
                                            background-color:#007bff;
                                            color:#ffffff;
                                            font-size:24px;
                                            letter-spacing:4px;
                                            padding:15px 30px;
                                            border-radius:6px;
                                            font-weight:bold;'>
                                            {code}
                                        </div>
                                    </td>
                                </tr>

                                <tr>
                                    <td style='font-size:14px;color:#666;'>
                                        <p>This OTP will expire in <strong>5 minutes</strong>.</p>
                                        <p>If you did not request this, please ignore this email.</p>
                                    </td>
                                </tr>

                                <tr>
                                    <td style='padding-top:20px;font-size:12px;color:#999;text-align:center;'>
                                        © {DateTime.Now.Year} Employee Management System
                                    </td>
                                </tr>

                            </table>

                        </td>
                    </tr>
                </table>

            </body>
            </html>";

            await SendEmailAsync(email, subject, body);
        }

        public async Task SendWelcomeEmailAsync(string email, string fullName)
        {
            var subject = "Welcome to Employee Management System";
            var body = $@"
            <!DOCTYPE html>
            <html>
            <body style='margin:0;padding:0;background-color:#f4f6f9;font-family:Arial,sans-serif;'>

                <table width='100%' cellpadding='0' cellspacing='0' style='padding:20px 0;'>
                    <tr>
                        <td align='center'>

                            <table width='600' cellpadding='0' cellspacing='0' 
                                   style='background:#ffffff;border-radius:8px;padding:30px;'>

                                <tr>
                                    <td align='center' style='padding-bottom:20px;'>
                                        <h2 style='color:#2c3e50;margin:0;'>
                                            🎉 Welcome to Employee Management System
                                        </h2>
                                    </td>
                                </tr>

                                <tr>
                                    <td style='font-size:16px;color:#333;'>
                                        <p>Hi <strong>{fullName}</strong>,</p>

                                        <p>
                                            Your account has been created successfully.
                                            We are excited to have you onboard!
                                        </p>

                                        <table width='100%' cellpadding='10' cellspacing='0' 
                                               style='background:#f8f9fa;border-radius:6px;margin:15px 0;'>
                                            <tr>
                                                <td>
                                                    <strong>Email:</strong> {email}
                                                </td>
                                            </tr>
                                        </table>

                                        <p>
                                            Please login and keep your password secure.
                                        </p>

                                        <p style='margin-top:25px;'>
                                            Best Regards,<br/>
                                            <strong>HR Team</strong><br/>
                                            Employee Management System
                                        </p>
                                    </td>
                                </tr>

                                <tr>
                                    <td style='padding-top:20px;font-size:12px;color:#999;text-align:center;'>
                                        © {DateTime.Now.Year} Employee Management System. All rights reserved.
                                    </td>
                                </tr>

                            </table>

                        </td>
                    </tr>
                </table>

            </body>
            </html>";

            await SendEmailAsync(email, subject, body);
        }

        private async Task SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                using var smtp = new SmtpClient(_config["Email:Smtp"], int.Parse(_config["Email:Port"]))
                {
                    Credentials = new NetworkCredential(
                        _config["Email:Username"],
                        _config["Email:Password"]
                    ),
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false
                };

                using var mail = new MailMessage
                {
                    From = new MailAddress(
                        _config["Email:Username"],
                        "Employee Management System"
                    ),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                mail.To.Add(to);

                await smtp.SendMailAsync(mail);
            }
            catch (Exception ex)
            {
                // log error properly in real project
                throw new Exception("Email sending failed", ex);
            }
        }
    }
}
